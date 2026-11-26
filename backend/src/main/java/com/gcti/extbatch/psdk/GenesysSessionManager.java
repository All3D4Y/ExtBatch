package com.gcti.extbatch.psdk;

import com.genesyslab.platform.applicationblocks.com.ConfServiceFactory;
import com.genesyslab.platform.applicationblocks.com.IConfService;
import com.genesyslab.platform.applicationblocks.warmstandby.WarmStandbyConfiguration;
import com.genesyslab.platform.applicationblocks.warmstandby.WarmStandbyService;
import com.genesyslab.platform.applicationblocks.warmstandby.WarmStandbyState;
import com.genesyslab.platform.commons.connection.configuration.PropertyConfiguration;
import com.genesyslab.platform.commons.protocol.ChannelClosedEvent;
import com.genesyslab.platform.commons.protocol.ChannelErrorEvent;
import com.genesyslab.platform.commons.protocol.ChannelListener;
import com.genesyslab.platform.commons.protocol.ChannelState;
import com.genesyslab.platform.commons.protocol.Endpoint;
import com.genesyslab.platform.configuration.protocol.ConfServerProtocol;
import com.gcti.extbatch.config.BackendProperties;
import com.gcti.extbatch.dto.CommonDtos.ConnectRequest;
import com.gcti.extbatch.dto.CommonDtos.ConnectResponse;
import com.gcti.extbatch.dto.CommonDtos.HealthResponse;
import java.util.EventObject;
import javax.annotation.PostConstruct;
import javax.annotation.PreDestroy;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Component;

@Component
public class GenesysSessionManager {
    private static final Logger log = LoggerFactory.getLogger(GenesysSessionManager.class);

    private final BackendProperties props;
    private final Object lock = new Object();

    private String curPrimaryHost;
    private int curPrimaryPort;
    private String curBackupHost;
    private int curBackupPort;
    private String curUserName;
    private String curPassword;
    private String curClientName;

    private ConfServerProtocol protocol;
    private WarmStandbyService warmStandby;
    private IConfService confService;
    private volatile String lastError = "";

    public GenesysSessionManager(BackendProperties props) {
        this.props = props;
        this.curPrimaryHost = props.getPrimaryHost();
        this.curPrimaryPort = props.getPrimaryPort();
        this.curBackupHost = props.getBackupHost();
        this.curBackupPort = props.getBackupPort();
        this.curUserName = props.getUserName();
        this.curPassword = props.getPassword();
        this.curClientName = props.getClientName();
    }

    @PostConstruct
    public void init() {
        if (props.isAutoConnectOnStartup()) {
            try {
                connect(null);
            } catch (Exception ex) {
                log.warn("Auto-connect on startup failed: {}", ex.getMessage());
            }
        }
    }

    @PreDestroy
    public void shutdown() {
        disconnect();
    }

    public int getTenantDbid() {
        return props.getTenantDbid();
    }

    public int getSwitchDbid() {
        return props.getSwitchDbid();
    }

    public String getSwitchName() {
        return props.getSwitchName();
    }

    public ConnectResponse connect(ConnectRequest req) throws Exception {
        synchronized (lock) {
            if (req != null) {
                if (notBlank(req.primaryHost)) curPrimaryHost = req.primaryHost.trim();
                if (req.primaryPort != null && req.primaryPort > 0) curPrimaryPort = req.primaryPort;
                if (notBlank(req.backupHost)) curBackupHost = req.backupHost.trim();
                if (req.backupPort != null && req.backupPort > 0) curBackupPort = req.backupPort;
                if (notBlank(req.userName)) curUserName = req.userName.trim();
                if (req.password != null && !req.password.isEmpty()) curPassword = req.password;
                if (notBlank(req.clientName)) curClientName = req.clientName.trim();
            }

            if (confService != null && protocol != null && protocol.getState() == ChannelState.Opened && req == null) {
                return buildConnectResponse("이미 Config Server에 연결되어 있습니다.");
            }

            closeInternal();
            openInternal();
            return buildConnectResponse("Config Server 연결 성공");
        }
    }

    public void disconnect() {
        synchronized (lock) {
            closeInternal();
            lastError = "";
        }
    }

    public IConfService ensureConnected() throws Exception {
        synchronized (lock) {
            if (confService != null && protocol != null && protocol.getState() == ChannelState.Opened) {
                return confService;
            }

            if (warmStandby != null && protocol != null
                    && (warmStandby.getState() == WarmStandbyState.RECONNECTING || warmStandby.getState() == WarmStandbyState.WAITING)) {
                for (int i = 0; i < 20; i++) {
                    if (protocol.getState() == ChannelState.Opened) {
                        return confService;
                    }
                    Thread.sleep(200);
                }
            }

            closeInternal();
            openInternal();
            return confService;
        }
    }

    public HealthResponse getHealth() {
        HealthResponse h = new HealthResponse();
        h.tenantDbid = props.getTenantDbid();
        h.switchDbid = props.getSwitchDbid();
        h.switchName = props.getSwitchName();
        synchronized (lock) {
            if (protocol != null) {
                ChannelState st = protocol.getState();
                h.channelState = st != null ? st.toString() : "Unknown";
                h.connected = (st == ChannelState.Opened);
                h.activeEndpoint = protocol.getEndpoint() != null ? protocol.getEndpoint().getUri().toString() : "";
            } else {
                h.channelState = ChannelState.Closed.toString();
                h.connected = false;
                h.activeEndpoint = "";
            }
            if (warmStandby != null && warmStandby.getState() != null) {
                h.warmStandbyState = warmStandby.getState().toString();
            } else {
                h.warmStandbyState = "Stopped";
            }
            h.lastError = lastError;
        }
        return h;
    }

    private void openInternal() throws Exception {
        String enc = notBlank(props.getStringsEncoding()) ? props.getStringsEncoding() : "euc-kr";

        PropertyConfiguration pConfig = new PropertyConfiguration();
        pConfig.setStringsEncoding(enc);

        Endpoint primary = new Endpoint("primary", curPrimaryHost, curPrimaryPort, pConfig);
        Endpoint backup = (notBlank(curBackupHost) && curBackupPort > 0)
                ? new Endpoint("backup", curBackupHost, curBackupPort, pConfig)
                : null;

        ConfServerProtocol proto = new ConfServerProtocol(primary);
        proto.setUserName(curUserName);
        proto.setUserPassword(curPassword);
        proto.setClientName(curClientName);
        // Important: do NOT force setClientApplicationType(CFGGenericClient) because 'default' app is CFGSCE (Code: 2230535)

        proto.addChannelListener(new ChannelListener() {
            @Override
            public void onChannelOpened(EventObject event) {
                lastError = "";
                log.info("Config Server channel opened: {}", proto.getEndpoint());
            }

            @Override
            public void onChannelClosed(ChannelClosedEvent event) {
                String cause = event != null && event.getCause() != null ? event.getCause().getMessage() : "Closed";
                log.warn("Config Server channel closed: {}", cause);
                if (cause != null && !cause.isEmpty()) {
                    lastError = cause;
                }
            }

            @Override
            public void onChannelError(ChannelErrorEvent event) {
                String msg = event != null && event.getCause() != null ? event.getCause().getMessage() : "ChannelError";
                log.error("Config Server channel error: {}", msg);
                lastError = msg;
            }
        });

        IConfService svc = ConfServiceFactory.createConfService(proto);
        WarmStandbyService ws = null;
        if (backup != null) {
            WarmStandbyConfiguration wsCfg = new WarmStandbyConfiguration(primary, backup);
            wsCfg.setTimeout(3000);
            wsCfg.setAttempts((short) 2);
            wsCfg.setSwitchovers((short) 2);
            wsCfg.setFirstReconnectTimeout(500);
            ws = new WarmStandbyService(proto);
            ws.applyConfiguration(wsCfg);
            ws.start();
        }

        try {
            proto.open();
        } catch (Exception primaryEx) {
            log.warn("Primary endpoint {}:{} open failed: {}", curPrimaryHost, curPrimaryPort, primaryEx.getMessage());
            if (backup != null) {
                try {
                    if (ws != null) ws.stop();
                    proto.setEndpoint(backup);
                    proto.open();
                    if (ws != null) {
                        WarmStandbyConfiguration fallbackCfg = new WarmStandbyConfiguration(backup, primary);
                        fallbackCfg.setTimeout(3000);
                        fallbackCfg.setAttempts((short) 2);
                        fallbackCfg.setSwitchovers((short) 2);
                        fallbackCfg.setFirstReconnectTimeout(500);
                        ws.applyConfiguration(fallbackCfg);
                        ws.start();
                    }
                    log.info("Connected via backup endpoint {}:{}", curBackupHost, curBackupPort);
                } catch (Exception backupEx) {
                    lastError = backupEx.getMessage();
                    safeClose(ws, proto, svc);
                    throw new IllegalStateException("Config Server Primary(" + curPrimaryHost + ":" + curPrimaryPort
                            + ") 및 Backup(" + curBackupHost + ":" + curBackupPort + ") 연결 실패: " + backupEx.getMessage(), backupEx);
                }
            } else {
                lastError = primaryEx.getMessage();
                safeClose(ws, proto, svc);
                throw primaryEx;
            }
        }

        if (proto.getState() != ChannelState.Opened) {
            safeClose(ws, proto, svc);
            throw new IllegalStateException("Config Server 연결 상태가 Opened가 아닙니다: " + proto.getState());
        }

        this.protocol = proto;
        this.warmStandby = ws;
        this.confService = svc;
        this.lastError = "";
    }

    private void closeInternal() {
        safeClose(this.warmStandby, this.protocol, this.confService);
        this.warmStandby = null;
        this.protocol = null;
        this.confService = null;
    }

    private static void safeClose(WarmStandbyService ws, ConfServerProtocol proto, IConfService svc) {
        if (ws != null) {
            try { ws.stop(); } catch (Exception ignored) {}
        }
        if (proto != null) {
            try {
                if (proto.getState() != ChannelState.Closed) {
                    proto.close();
                }
            } catch (Exception ignored) {}
        }
        if (svc != null) {
            try { ConfServiceFactory.releaseConfService(svc); } catch (Exception ignored) {}
        }
    }

    private ConnectResponse buildConnectResponse(String msg) {
        ConnectResponse r = new ConnectResponse();
        r.tenantDbid = props.getTenantDbid();
        r.switchDbid = props.getSwitchDbid();
        r.message = msg;
        if (protocol != null) {
            ChannelState st = protocol.getState();
            r.channelState = st != null ? st.toString() : "Unknown";
            r.connected = (st == ChannelState.Opened);
            r.activeEndpoint = protocol.getEndpoint() != null ? protocol.getEndpoint().getUri().toString() : "";
        }
        if (warmStandby != null && warmStandby.getState() != null) {
            r.warmStandbyState = warmStandby.getState().toString();
        }
        return r;
    }

    private static boolean notBlank(String s) {
        return s != null && !s.trim().isEmpty();
    }
}
