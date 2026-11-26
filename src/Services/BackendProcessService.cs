using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CmOxeProvisioner.Models;

namespace CmOxeProvisioner.Services
{
    public sealed class BackendProcessService : IDisposable
    {
        private readonly string _baseDir;
        private readonly Func<AppSettings> _getSettings;
        private readonly string _logDir;
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);
        private readonly object _logLock = new object();

        private Process _process;
        private IntPtr _jobHandle = IntPtr.Zero;
        private bool _ownsProcess = false;
        private int _activePort = 18080;
        private bool _disposed = false;

        public BackendProcessService(string baseDir, string logDir, Func<AppSettings> getSettings)
        {
            _baseDir = baseDir ?? AppDomain.CurrentDomain.BaseDirectory;
            _logDir = logDir ?? Path.Combine(_baseDir, "logs");
            _getSettings = getSettings;
        }

        public int ActivePort
        {
            get { return _activePort; }
        }

        public string BaseUrl
        {
            get { return "http://127.0.0.1:" + _activePort; }
        }

        public bool IsRunning
        {
            get
            {
                if (_process != null && !_process.HasExited) return true;
                return ProbeHealth(_activePort, 400);
            }
        }

        public async Task<string> EnsureRunningAsync()
        {
            if (_disposed) throw new ObjectDisposedException("BackendProcessService");

            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_process != null && !_process.HasExited && ProbeHealth(_activePort, 600))
                {
                    return BaseUrl;
                }

                AppSettings settings = _getSettings != null ? _getSettings() : AppSettings.CreateDefault();
                int preferredPort = (settings != null && settings.BackendPort > 0 && settings.BackendPort <= 65535)
                    ? settings.BackendPort
                    : 18080;

                // If our own process exited unexpectedly, reset handle before relaunching
                CleanupDeadProcess();

                // Check if preferred port already hosts an active extbatch-backend
                if (ProbeHealth(preferredPort, 700))
                {
                    _activePort = preferredPort;
                    _ownsProcess = false;
                    WriteBackendLog("[Launcher] 기존 extbatch-backend 인스턴스 재사용 (port=" + preferredPort + ")");
                    return BaseUrl;
                }

                int targetPort = IsPortAvailable(preferredPort) ? preferredPort : FindFreeLocalPort();
                if (targetPort != preferredPort)
                {
                    WriteBackendLog("[Launcher] 설정 포트(" + preferredPort + ") 사용 중 -> 자동 할당 포트(" + targetPort + ")");
                }

                StartBackendProcess(targetPort);
                bool healthy = await WaitForHealthAsync(targetPort, 25000).ConfigureAwait(false);
                if (!healthy)
                {
                    string exitNote = (_process != null && _process.HasExited)
                        ? (" (종료코드=" + _process.ExitCode + ")")
                        : "";
                    StopOwnedProcessInternal();
                    throw new InvalidOperationException(
                        "로컬 Java 백엔드 서버 기동에 실패했습니다" + exitNote + ". logs\\backend 로그를 확인하세요.");
                }

                _activePort = targetPort;
                return BaseUrl;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task ShutdownAsync()
        {
            if (_disposed) return;

            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                ShutdownInternal();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public void ShutdownSync()
        {
            if (_disposed) return;

            bool locked = _lifecycleLock.Wait(1500);
            try
            {
                ShutdownInternal();
            }
            finally
            {
                if (locked) _lifecycleLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            ShutdownSync();
            _disposed = true;
            _lifecycleLock.Dispose();
        }

        private void ShutdownInternal()
        {
            try
            {
                // Always ask backend to cleanly close Genesys Config Server channel on exit
                if (ProbeHealth(_activePort, 500))
                {
                    PostDisconnectQuietly(_activePort);
                }
            }
            catch
            {
            }

            if (_ownsProcess)
            {
                StopOwnedProcessInternal();
            }
            CloseJobHandle();
        }

        private void StartBackendProcess(int port)
        {
            string backendDir = ResolveBackendDir();
            if (string.IsNullOrEmpty(backendDir))
            {
                throw new FileNotFoundException("backend 디렉터리 및 extbatch-backend.jar를 찾을 수 없습니다.");
            }

            string jarPath = Path.Combine(backendDir, "extbatch-backend.jar");
            string libDir = Path.Combine(backendDir, "lib");
            if (!File.Exists(jarPath) || !Directory.Exists(libDir))
            {
                throw new FileNotFoundException("백엔드 실행 파일(extbatch-backend.jar 또는 lib\\)이 누락되었습니다: " + backendDir);
            }

            string javaExe = ResolveJavaExe();
            if (string.IsNullOrEmpty(javaExe) || !File.Exists(javaExe))
            {
                throw new FileNotFoundException("Java 8 실행 파일(javaw.exe / java.exe)을 찾을 수 없습니다. C:\\dev\\jdk8 또는 JAVA_HOME을 확인하세요.");
            }

            string customersPath = ResolveCustomersJsonPath(backendDir);
            string snapshotsPath = Path.Combine(
                string.IsNullOrEmpty(_baseDir) ? Path.GetFullPath(Path.Combine(backendDir, "..")) : _baseDir,
                "snapshots");
            string classpath = jarPath + ";" + Path.Combine(libDir, "*");
            string args = string.Format(
                "-Dfile.encoding=UTF-8 -Xms128m -Xmx512m -cp \"{0}\" com.gcti.extbatch.ExtBatchBackendApplication --server.address=127.0.0.1 --server.port={1} --genesys.auto-connect-on-startup=false --extbatch.customers-json-path=\"{2}\" --extbatch.snapshots-dir=\"{3}\"",
                classpath,
                port,
                customersPath,
                snapshotsPath);

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = javaExe;
            psi.Arguments = args;
            psi.WorkingDirectory = backendDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            WriteBackendLog("[Launcher] 기동 시작: \"" + javaExe + "\" port=" + port + " dir=" + backendDir);

            EnsureJobObject();

            Process proc = new Process();
            proc.StartInfo = psi;
            proc.EnableRaisingEvents = true;
            proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.Data)) WriteBackendLog(e.Data);
            };
            proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.Data)) WriteBackendLog("[STDERR] " + e.Data);
            };

            if (!proc.Start())
            {
                throw new InvalidOperationException("Java 백엔드 프로세스를 시작하지 못했습니다.");
            }

            _process = proc;
            _ownsProcess = true;
            _activePort = port;

            // Bind backend child process to Windows Job Object so OS kills it automatically if ExtBatch crashes
            if (_jobHandle != IntPtr.Zero)
            {
                try
                {
                    AssignProcessToJobObject(_jobHandle, proc.Handle);
                }
                catch
                {
                }
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }

        private async Task<bool> WaitForHealthAsync(int port, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (_process != null && _process.HasExited)
                {
                    return false;
                }
                if (ProbeHealth(port, 700))
                {
                    return true;
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
            return false;
        }

        public static bool ProbeHealth(int port, int timeoutMs)
        {
            if (port <= 0 || port > 65535) return false;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/api/health");
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.KeepAlive = false;
                req.Proxy = null;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    if ((int)resp.StatusCode != 200) return false;
                    using (Stream stream = resp.GetResponseStream())
                    {
                        if (stream == null) return false;
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string json = reader.ReadToEnd();
                            if (string.IsNullOrWhiteSpace(json)) return false;
                            JavaScriptSerializer ser = new JavaScriptSerializer();
                            Dictionary<string, object> dict = ser.Deserialize<Dictionary<string, object>>(json);
                            if (dict == null) return false;
                            object statusObj, svcObj;
                            bool hasStatus = dict.TryGetValue("status", out statusObj);
                            bool hasSvc = dict.TryGetValue("service", out svcObj);
                            return hasStatus && string.Equals(Convert.ToString(statusObj), "UP", StringComparison.OrdinalIgnoreCase)
                                && hasSvc && string.Equals(Convert.ToString(svcObj), "extbatch-backend", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static void PostDisconnectQuietly(int port)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/api/cm/disconnect");
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.ContentLength = 0;
                req.Timeout = 1000;
                req.ReadWriteTimeout = 1000;
                req.KeepAlive = false;
                req.Proxy = null;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                }
            }
            catch
            {
            }
        }

        private static bool IsPortAvailable(int port)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (listener != null)
                {
                    try { listener.Stop(); } catch { }
                }
            }
        }

        private static int FindFreeLocalPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private string ResolveBackendDir()
        {
            List<string> candidates = new List<string>();
            if (!string.IsNullOrEmpty(_baseDir))
            {
                candidates.Add(Path.Combine(_baseDir, "backend"));
                candidates.Add(Path.GetFullPath(Path.Combine(_baseDir, "..", "backend")));
            }
            string asmBase = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(asmBase))
            {
                candidates.Add(Path.Combine(asmBase, "backend"));
                candidates.Add(Path.GetFullPath(Path.Combine(asmBase, "..", "backend")));
                candidates.Add(Path.GetFullPath(Path.Combine(asmBase, "..", "..", "backend")));
            }

            foreach (string dir in candidates)
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, "extbatch-backend.jar")) && Directory.Exists(Path.Combine(dir, "lib")))
                    {
                        return dir;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        private string ResolveCustomersJsonPath(string backendDir)
        {
            string[] candidates = new string[]
            {
                Path.Combine(_baseDir, "cm_customers.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cm_customers.json"),
                Path.GetFullPath(Path.Combine(backendDir, "..", "cm_customers.json"))
            };
            foreach (string p in candidates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                }
                catch
                {
                }
            }
            return Path.GetFullPath(Path.Combine(backendDir, "..", "cm_customers.json"));
        }

        private static string ResolveJavaExe()
        {
            string envOverride = Environment.GetEnvironmentVariable("EXTBATCH_JAVA_EXE");
            if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride.Trim()))
            {
                return envOverride.Trim();
            }

            List<string> candidates = new List<string>();
            candidates.Add(@"C:\dev\jdk8\bin\javaw.exe");
            candidates.Add(@"C:\dev\jdk8\bin\java.exe");

            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(javaHome))
            {
                candidates.Add(Path.Combine(javaHome.Trim(), "bin", "javaw.exe"));
                candidates.Add(Path.Combine(javaHome.Trim(), "bin", "java.exe"));
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = dir.Trim().Trim('"');
                if (string.IsNullOrEmpty(trimmed)) continue;
                candidates.Add(Path.Combine(trimmed, "javaw.exe"));
                candidates.Add(Path.Combine(trimmed, "java.exe"));
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
            return null;
        }

        private void CleanupDeadProcess()
        {
            if (_process != null)
            {
                try
                {
                    if (_process.HasExited)
                    {
                        _process.Dispose();
                        _process = null;
                        _ownsProcess = false;
                    }
                }
                catch
                {
                    _process = null;
                    _ownsProcess = false;
                }
            }
        }

        private void StopOwnedProcessInternal()
        {
            if (_process == null) return;
            try
            {
                if (!_process.HasExited)
                {
                    WriteBackendLog("[Launcher] 백엔드 프로세스 종료 중 (pid=" + _process.Id + ")");
                    try { _process.Kill(); } catch { }
                    try { _process.WaitForExit(2000); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                try { _process.Dispose(); } catch { }
                _process = null;
                _ownsProcess = false;
            }
        }

        private void WriteBackendLog(string line)
        {
            if (string.IsNullOrEmpty(_logDir) || string.IsNullOrEmpty(line)) return;
            try
            {
                lock (_logLock)
                {
                    if (!Directory.Exists(_logDir)) Directory.CreateDirectory(_logDir);
                    string path = Path.Combine(_logDir, "backend_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        private void EnsureJobObject()
        {
            if (_jobHandle != IntPtr.Zero) return;
            try
            {
                IntPtr job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) return;

                JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

                int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
                    {
                        _jobHandle = job;
                        job = IntPtr.Zero;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                    if (job != IntPtr.Zero) CloseHandle(job);
                }
            }
            catch
            {
            }
        }

        private void CloseJobHandle()
        {
            if (_jobHandle != IntPtr.Zero)
            {
                try { CloseHandle(_jobHandle); } catch { }
                _jobHandle = IntPtr.Zero;
            }
        }

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            IntPtr hJob,
            int infoClass,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
