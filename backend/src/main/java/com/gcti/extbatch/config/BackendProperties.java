package com.gcti.extbatch.config;

import org.springframework.boot.context.properties.ConfigurationProperties;

@ConfigurationProperties(prefix = "genesys")
public class BackendProperties {
    private String primaryHost = "10.62.130.67";
    private int primaryPort = 2020;
    private String backupHost = "10.62.130.68";
    private int backupPort = 2020;
    private String userName = "default";
    private String password = "password";
    private String clientName = "default";
    private String stringsEncoding = "euc-kr";
    private int tenantDbid = 1;
    private String switchName = "TServerAlcatel";
    private int switchDbid = 101;
    private boolean autoConnectOnStartup = false;

    public String getPrimaryHost() { return primaryHost; }
    public void setPrimaryHost(String primaryHost) { this.primaryHost = primaryHost; }

    public int getPrimaryPort() { return primaryPort; }
    public void setPrimaryPort(int primaryPort) { this.primaryPort = primaryPort; }

    public String getBackupHost() { return backupHost; }
    public void setBackupHost(String backupHost) { this.backupHost = backupHost; }

    public int getBackupPort() { return backupPort; }
    public void setBackupPort(int backupPort) { this.backupPort = backupPort; }

    public String getUserName() { return userName; }
    public void setUserName(String userName) { this.userName = userName; }

    public String getPassword() { return password; }
    public void setPassword(String password) { this.password = password; }

    public String getClientName() { return clientName; }
    public void setClientName(String clientName) { this.clientName = clientName; }

    public String getStringsEncoding() { return stringsEncoding; }
    public void setStringsEncoding(String stringsEncoding) { this.stringsEncoding = stringsEncoding; }

    public int getTenantDbid() { return tenantDbid; }
    public void setTenantDbid(int tenantDbid) { this.tenantDbid = tenantDbid; }

    public String getSwitchName() { return switchName; }
    public void setSwitchName(String switchName) { this.switchName = switchName; }

    public int getSwitchDbid() { return switchDbid; }
    public void setSwitchDbid(int switchDbid) { this.switchDbid = switchDbid; }

    public boolean isAutoConnectOnStartup() { return autoConnectOnStartup; }
    public void setAutoConnectOnStartup(boolean autoConnectOnStartup) { this.autoConnectOnStartup = autoConnectOnStartup; }
}
