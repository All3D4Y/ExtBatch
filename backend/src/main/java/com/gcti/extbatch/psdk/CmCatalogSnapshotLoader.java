package com.gcti.extbatch.psdk;

import com.genesyslab.platform.applicationblocks.com.IConfService;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentLogin;
import com.genesyslab.platform.applicationblocks.com.objects.CfgAgentLoginInfo;
import com.genesyslab.platform.applicationblocks.com.objects.CfgDN;
import com.genesyslab.platform.applicationblocks.com.objects.CfgDNGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgDNInfo;
import com.genesyslab.platform.applicationblocks.com.objects.CfgFolder;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPerson;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPlace;
import com.genesyslab.platform.applicationblocks.com.objects.CfgPlaceGroup;
import com.genesyslab.platform.applicationblocks.com.objects.CfgScript;
import com.genesyslab.platform.applicationblocks.com.objects.CfgSkill;
import com.genesyslab.platform.applicationblocks.com.objects.CfgSkillLevel;
import com.genesyslab.platform.applicationblocks.com.objects.CfgSwitch;
import com.genesyslab.platform.applicationblocks.com.queries.CfgAgentGroupQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgAgentLoginQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgDNGroupQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgDNQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgPersonQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgPlaceGroupQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgPlaceQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgScriptQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgSkillQuery;
import com.genesyslab.platform.applicationblocks.com.queries.CfgSwitchQuery;
import com.genesyslab.platform.commons.collections.KeyValueCollection;
import com.genesyslab.platform.commons.collections.KeyValuePair;
import com.genesyslab.platform.configuration.protocol.types.CfgDNType;
import com.gcti.extbatch.psdk.CmFolderResolver.FolderIndex;
import com.gcti.extbatch.psdk.CustomerCatalogService.CustomerRecord;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.TreeMap;
import org.springframework.stereotype.Component;

@Component
public class CmCatalogSnapshotLoader {

    private static final long CACHE_TTL_MS = 12_000L;

    public static class LoadedCatalog {
        public final long loadedAtMs;
        public final boolean fromCache;
        public final FolderIndex folderIdx;
        public final List<CustomerRecord> customers;
        public final Map<Integer, String> folderDbidToCustomer;
        public final Map<String, CustomerRecord> customersByNameLower = new HashMap<>();
        public final Map<Integer, String> switchNamesByDbid = new HashMap<>();
        public final List<CfgFolder> allFolders = new ArrayList<>();
        public final List<CfgDN> dns = new ArrayList<>();
        public final List<CfgPlace> places = new ArrayList<>();
        public final List<CfgAgentLogin> logins = new ArrayList<>();
        public final List<CfgPerson> persons = new ArrayList<>();
        public final List<CfgPlaceGroup> placeGroups = new ArrayList<>();
        public final List<CfgAgentGroup> agentGroups = new ArrayList<>();
        public final List<CfgDNGroup> dnGroups = new ArrayList<>();
        public final List<CfgScript> scripts = new ArrayList<>();
        public final List<CfgSkill> skills = new ArrayList<>();

        public final Map<Integer, CfgDN> dnByDbid = new HashMap<>();
        public final Map<Integer, CfgPlace> placeByDbid = new HashMap<>();
        public final Map<Integer, CfgAgentLogin> loginByDbid = new HashMap<>();
        public final Map<Integer, CfgPerson> personByDbid = new HashMap<>();
        public final Map<Integer, String> skillNameByDbid = new HashMap<>();

        public final Map<Integer, List<CfgPlace>> dnDbidToPlaces = new HashMap<>();
        public final Map<Integer, List<String>> dnDbidToDnGroups = new HashMap<>();
        public final Map<Integer, List<String>> placeDbidToPlaceGroups = new HashMap<>();
        public final Map<Integer, List<CfgPerson>> loginDbidToPersons = new HashMap<>();
        public final Map<Integer, List<String>> personDbidToAgentGroups = new HashMap<>();

        LoadedCatalog(long loadedAtMs, boolean fromCache, FolderIndex folderIdx,
                      List<CustomerRecord> customers, Map<Integer, String> folderDbidToCustomer) {
            this.loadedAtMs = loadedAtMs;
            this.fromCache = fromCache;
            this.folderIdx = folderIdx;
            this.customers = customers;
            this.folderDbidToCustomer = folderDbidToCustomer;
            if (customers != null) {
                for (CustomerRecord c : customers) {
                    if (c != null && c.name != null && !c.name.trim().isEmpty()) {
                        customersByNameLower.put(c.name.trim().toLowerCase(Locale.ROOT), c);
                    }
                }
            }
        }

        private LoadedCatalog asCachedCopy() {
            LoadedCatalog copy = new LoadedCatalog(this.loadedAtMs, true, this.folderIdx, this.customers, this.folderDbidToCustomer);
            copy.switchNamesByDbid.putAll(this.switchNamesByDbid);
            copy.allFolders.addAll(this.allFolders);
            copy.dns.addAll(this.dns);
            copy.places.addAll(this.places);
            copy.logins.addAll(this.logins);
            copy.persons.addAll(this.persons);
            copy.placeGroups.addAll(this.placeGroups);
            copy.agentGroups.addAll(this.agentGroups);
            copy.dnGroups.addAll(this.dnGroups);
            copy.scripts.addAll(this.scripts);
            copy.skills.addAll(this.skills);
            copy.dnByDbid.putAll(this.dnByDbid);
            copy.placeByDbid.putAll(this.placeByDbid);
            copy.loginByDbid.putAll(this.loginByDbid);
            copy.personByDbid.putAll(this.personByDbid);
            copy.skillNameByDbid.putAll(this.skillNameByDbid);
            copy.dnDbidToPlaces.putAll(this.dnDbidToPlaces);
            copy.dnDbidToDnGroups.putAll(this.dnDbidToDnGroups);
            copy.placeDbidToPlaceGroups.putAll(this.placeDbidToPlaceGroups);
            copy.loginDbidToPersons.putAll(this.loginDbidToPersons);
            copy.personDbidToAgentGroups.putAll(this.personDbidToAgentGroups);
            return copy;
        }

        public String resolveFolderDisplayPath(String objectPath, Integer folderDbid) {
            if (folderDbid != null && folderDbid > 0) {
                String p = folderIdx.resolveDisplayPath(folderDbid);
                if (!p.isEmpty()) return p;
            }
            return CmFolderResolver.toDisplayPath(objectPath);
        }

        public String inferCustomer(Integer folderDbid, String objectNameFallback) {
            if (folderDbid != null && folderDbid > 0) {
                String hit = folderDbidToCustomer.get(folderDbid);
                if (hit != null && !hit.isEmpty()) return hit;
                String disp = folderIdx.resolveDisplayPath(folderDbid);
                if (!disp.isEmpty()) {
                    String matched = matchPathTestOrCenter(disp);
                    if (!matched.isEmpty()) return matched;
                }
            }
            if (objectNameFallback != null && !objectNameFallback.trim().isEmpty()) {
                CustomerRecord cr = customersByNameLower.get(objectNameFallback.trim().toLowerCase(Locale.ROOT));
                if (cr != null) return cr.name;
            }
            return "";
        }

        private String matchPathTestOrCenter(String dispPath) {
            String[] segs = dispPath.split(" > ");
            for (int i = segs.length - 1; i >= 0; i--) {
                String seg = segs[i].trim();
                if ("TEST".equalsIgnoreCase(seg) && dispPath.contains(" > ")) {
                    for (CustomerRecord c : customers) {
                        if (c.name != null && c.name.contains("__TEST__")) {
                            return c.name;
                        }
                    }
                }
                CustomerRecord cr = customersByNameLower.get(seg.toLowerCase(Locale.ROOT));
                if (cr != null) return cr.name;
            }
            return "";
        }

        public String resolveSwitchName(Integer swDbid) {
            if (swDbid == null) return "";
            String name = switchNamesByDbid.get(swDbid);
            return name != null ? name : ("Switch#" + swDbid);
        }

        public int totalObjectCount() {
            return dns.size() + places.size() + logins.size() + persons.size()
                    + placeGroups.size() + agentGroups.size() + dnGroups.size() + scripts.size();
        }
    }

    private final GenesysSessionManager sessionManager;
    private final CmFolderResolver folderResolver;
    private final CustomerCatalogService customerCatalog;
    private final Object cacheLock = new Object();
    private LoadedCatalog cachedCatalog;

    public CmCatalogSnapshotLoader(
            GenesysSessionManager sessionManager,
            CmFolderResolver folderResolver,
            CustomerCatalogService customerCatalog) {
        this.sessionManager = sessionManager;
        this.folderResolver = folderResolver;
        this.customerCatalog = customerCatalog;
    }

    public void invalidateCache() {
        synchronized (cacheLock) {
            cachedCatalog = null;
        }
    }

    public LoadedCatalog load(boolean forceRefresh, boolean includeGroupsAndScripts) throws Exception {
        synchronized (cacheLock) {
            long now = System.currentTimeMillis();
            if (!forceRefresh && cachedCatalog != null && (now - cachedCatalog.loadedAtMs) < CACHE_TTL_MS) {
                return cachedCatalog.asCachedCopy();
            }
        }

        IConfService service = sessionManager.ensureConnected();
        int tenantDbid = sessionManager.getTenantDbid();

        FolderIndex folderIdx = folderResolver.loadIndex(service);
        List<CustomerRecord> customers = customerCatalog.loadAll();
        Map<Integer, String> folderCustMap = folderResolver.buildFolderDbidToCustomerMap(
                folderIdx, customers, sessionManager.getSwitchName());

        LoadedCatalog cat = new LoadedCatalog(System.currentTimeMillis(), false, folderIdx, customers, folderCustMap);

        CfgSwitchQuery swQ = new CfgSwitchQuery(service);
        swQ.setTenantDbid(tenantDbid);
        Collection<CfgSwitch> switches = swQ.execute();
        if (switches != null) {
            for (CfgSwitch sw : switches) {
                if (sw.getDBID() != null) {
                    cat.switchNamesByDbid.put(sw.getDBID(), sw.getName() != null ? sw.getName() : "");
                }
            }
        }
        cat.switchNamesByDbid.putIfAbsent(sessionManager.getSwitchDbid(), sessionManager.getSwitchName());
        cat.allFolders.addAll(folderIdx.getAllFolders());

        // Populate allFolders directly via FolderIndex DBIDs by scanning DNs/etc? We can collect folders by querying CfgFolderQuery or iterating FolderIndex.
        // FolderIndex already holds all CfgFolder objects; expose via folderIdx
        loadDNs(service, tenantDbid, cat);
        loadPlaces(service, tenantDbid, cat);
        loadAgentLogins(service, tenantDbid, cat);
        loadPersons(service, tenantDbid, cat);

        if (includeGroupsAndScripts) {
            loadSkills(service, tenantDbid, cat);
            loadPlaceGroups(service, tenantDbid, cat);
            loadAgentGroups(service, tenantDbid, cat);
            loadDnGroups(service, tenantDbid, cat);
            loadScripts(service, tenantDbid, cat);
        }

        synchronized (cacheLock) {
            if (includeGroupsAndScripts) {
                this.cachedCatalog = cat;
            }
        }
        return cat;
    }

    private static void loadDNs(IConfService service, int tenantDbid, LoadedCatalog cat) throws Exception {
        CfgDNQuery q = new CfgDNQuery(service);
        q.setTenantDbid(tenantDbid);
        Collection<CfgDN> res = q.execute();
        if (res == null) return;
        for (CfgDN dn : res) {
            cat.dns.add(dn);
            if (dn.getDBID() != null) {
                cat.dnByDbid.put(dn.getDBID(), dn);
            }
        }
    }

    private static void loadPlaces(IConfService service, int tenantDbid, LoadedCatalog cat) throws Exception {
        CfgPlaceQuery q = new CfgPlaceQuery(service);
        q.setTenantDbid(tenantDbid);
        Collection<CfgPlace> res = q.execute();
        if (res == null) return;
        for (CfgPlace p : res) {
            cat.places.add(p);
            if (p.getDBID() != null) {
                cat.placeByDbid.put(p.getDBID(), p);
            }
            if (p.getDNDBIDs() != null) {
                for (Integer dnDbid : p.getDNDBIDs()) {
                    if (dnDbid != null && dnDbid > 0) {
                        cat.dnDbidToPlaces.computeIfAbsent(dnDbid, k -> new ArrayList<>()).add(p);
                    }
                }
            }
        }
    }

    private static void loadAgentLogins(IConfService service, int tenantDbid, LoadedCatalog cat) throws Exception {
        CfgAgentLoginQuery q = new CfgAgentLoginQuery(service);
        q.setTenantDbid(tenantDbid);
        Collection<CfgAgentLogin> res = q.execute();
        if (res == null) return;
        for (CfgAgentLogin al : res) {
            cat.logins.add(al);
            if (al.getDBID() != null) {
                cat.loginByDbid.put(al.getDBID(), al);
            }
        }
    }

    private static void loadPersons(IConfService service, int tenantDbid, LoadedCatalog cat) throws Exception {
        CfgPersonQuery q = new CfgPersonQuery(service);
        q.setTenantDbid(tenantDbid);
        Collection<CfgPerson> res = q.execute();
        if (res == null) return;
        for (CfgPerson p : res) {
            cat.persons.add(p);
            if (p.getDBID() != null) {
                cat.personByDbid.put(p.getDBID(), p);
            }
            if (p.getAgentInfo() != null && p.getAgentInfo().getAgentLogins() != null) {
                for (CfgAgentLoginInfo ali : p.getAgentInfo().getAgentLogins()) {
                    Integer lDbid = ali.getAgentLoginDBID();
                    if (lDbid != null && lDbid > 0) {
                        cat.loginDbidToPersons.computeIfAbsent(lDbid, k -> new ArrayList<>()).add(p);
                    }
                }
            }
        }
    }

    private static void loadSkills(IConfService service, int tenantDbid, LoadedCatalog cat) throws Exception {
        CfgSkillQuery q = new CfgSkillQuery(service);
        q.setTenantDbid(tenantDbid);
        Collection<CfgSkill> res = q.execute();
        if (res == null) return;
        for (CfgSkill sk : res) {
            cat.skills.add(sk);
            if (sk.getDBID() != null && sk.getName() != null) {
                cat.skillNameByDbid.put(sk.getDBID(), sk.getName());
            }
        }
    }

    private static void loadPlaceGroups(IConfService service, int tenantDbid, LoadedCatalog cat) throws Exception {
        CfgPlaceGroupQuery q = new CfgPlaceGroupQuery(service);
        q.setTenantDbid(tenantDbid);
        Collection<CfgPlaceGroup> res = q.execute();
        if (res == null) return;
        for (CfgPlaceGroup pg : res) {
            cat.placeGroups.add(pg);
            String pgName = pg.getGroupInfo() != null ? safe(pg.getGroupInfo().getName()) : "";
            if (pg.getPlaceDBIDs() != null) {
                for (Integer pDbid : pg.getPlaceDBIDs()) {
                    if (pDbid != null && pDbid > 0) {
                        cat.placeDbidToPlaceGroups.computeIfAbsent(pDbid, k -> new ArrayList<>()).add(pgName);
                    }
                }
            }
        }
    }

    private static void loadAgentGroups(IConfService service, int tenantDbid, LoadedCatalog cat) throws Exception {
        CfgAgentGroupQuery q = new CfgAgentGroupQuery(service);
        q.setTenantDbid(tenantDbid);
        Collection<CfgAgentGroup> res = q.execute();
        if (res == null) return;
        for (CfgAgentGroup ag : res) {
            cat.agentGroups.add(ag);
            String agName = ag.getGroupInfo() != null ? safe(ag.getGroupInfo().getName()) : "";
            if (ag.getAgentDBIDs() != null) {
                for (Integer aDbid : ag.getAgentDBIDs()) {
                    if (aDbid != null && aDbid > 0) {
                        cat.personDbidToAgentGroups.computeIfAbsent(aDbid, k -> new ArrayList<>()).add(agName);
                    }
                }
            }
        }
    }

    private static void loadDnGroups(IConfService service, int tenantDbid, LoadedCatalog cat) throws Exception {
        CfgDNGroupQuery q = new CfgDNGroupQuery(service);
        q.setTenantDbid(tenantDbid);
        Collection<CfgDNGroup> res = q.execute();
        if (res == null) return;
        for (CfgDNGroup dg : res) {
            cat.dnGroups.add(dg);
            String dgName = dg.getGroupInfo() != null ? safe(dg.getGroupInfo().getName()) : "";
            if (dg.getDNs() != null) {
                for (CfgDNInfo info : dg.getDNs()) {
                    Integer dnDbid = info.getDNDBID();
                    if (dnDbid != null && dnDbid > 0) {
                        cat.dnDbidToDnGroups.computeIfAbsent(dnDbid, k -> new ArrayList<>()).add(dgName);
                    }
                }
            }
        }
    }

    private static void loadScripts(IConfService service, int tenantDbid, LoadedCatalog cat) throws Exception {
        CfgScriptQuery q = new CfgScriptQuery(service);
        q.setTenantDbid(tenantDbid);
        Collection<CfgScript> res = q.execute();
        if (res == null) return;
        cat.scripts.addAll(res);
    }

    public static String mapDnCategory(CfgDNType type) {
        if (type == null) return "DN";
        if (type == CfgDNType.CFGExtension) return "Agent DN";
        if (type == CfgDNType.CFGACDPosition) return "ACD Position";
        if (type == CfgDNType.CFGRoutingPoint || type == CfgDNType.CFGExtRoutingPoint) return "RSI";
        if (type == CfgDNType.CFGVirtACDQueue || type == CfgDNType.CFGVirtRoutingPoint || type == CfgDNType.CFGACDQueue) return "VQueue";
        return "DN";
    }

    public static String formatSkillSummary(CfgPerson p, Map<Integer, String> skillNameByDbid) {
        if (p == null || p.getAgentInfo() == null || p.getAgentInfo().getSkillLevels() == null) return "";
        List<String> parts = new ArrayList<>();
        for (CfgSkillLevel sl : p.getAgentInfo().getSkillLevels()) {
            Integer skId = sl.getSkillDBID();
            String skName = skId != null ? skillNameByDbid.getOrDefault(skId, "Skill#" + skId) : "?";
            parts.add(skName + ":" + sl.getLevel());
        }
        Collections.sort(parts);
        return String.join(", ", parts);
    }

    public static String flattenUserProperties(KeyValueCollection kv) {
        if (kv == null || kv.size() == 0) return "";
        Map<String, String> sorted = new TreeMap<>();
        for (Object o : kv) {
            if (o instanceof KeyValuePair) {
                KeyValuePair pair = (KeyValuePair) o;
                String k = pair.getStringKey();
                if (k != null) {
                    Object raw = pair.getValue();
                    String valStr = (raw instanceof KeyValueCollection)
                            ? ("[" + flattenUserProperties((KeyValueCollection) raw) + "]")
                            : (pair.getValueAsString() != null ? pair.getValueAsString() : "");
                    sorted.put(k, valStr);
                }
            }
        }
        List<String> lines = new ArrayList<>();
        for (Map.Entry<String, String> e : sorted.entrySet()) {
            lines.add(e.getKey() + "=" + e.getValue());
        }
        return String.join("; ", lines);
    }

    public static String safe(String s) {
        return s == null ? "" : s.trim();
    }
}
