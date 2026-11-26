package com.gcti.extbatch.psdk;

import com.genesyslab.platform.applicationblocks.com.IConfService;
import com.genesyslab.platform.applicationblocks.com.objects.CfgFolder;
import com.genesyslab.platform.applicationblocks.com.objects.CfgOwnerID;
import com.genesyslab.platform.applicationblocks.com.queries.CfgFolderQuery;
import com.genesyslab.platform.configuration.protocol.types.CfgFolderClass;
import com.gcti.extbatch.psdk.CustomerCatalogService.CustomerRecord;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collection;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import org.springframework.stereotype.Component;

@Component
public class CmFolderResolver {

    private static final Map<String, List<String>> PERSON_FOLDER_OVERRIDES = new HashMap<>();
    static {
        PERSON_FOLDER_OVERRIDES.put("하이프라자_온라인몰", Arrays.asList("24층", "하이프라자온라인몰"));
        PERSON_FOLDER_OVERRIDES.put("LG하우시스_신유통", Arrays.asList("25층", "LG하우시스_신유통"));
        PERSON_FOLDER_OVERRIDES.put("버킷스토어", Arrays.asList("LB휴넷18층", "버킷스토어"));
        PERSON_FOLDER_OVERRIDES.put("한국교육과정평가원", Arrays.asList("26층", "한국교육과정평가원"));
        PERSON_FOLDER_OVERRIDES.put("LG하우시스_알루미늄", Arrays.asList("충정로", "LX하우시스_알루미늄"));
        PERSON_FOLDER_OVERRIDES.put("LG통합ID", Arrays.asList("마곡LG사이언스파크", "LGID"));
    }

    public static class FolderIndex {
        private final Map<Integer, CfgFolder> byDbid = new HashMap<>();
        private final Map<String, CfgFolder> byExactPath = new HashMap<>();
        private final Map<String, CfgFolder> byLowerPath = new HashMap<>();
        private final Map<Integer, List<CfgFolder>> childrenByParentDbid = new HashMap<>();

        public CfgFolder getByDbid(Integer dbid) {
            return dbid == null ? null : byDbid.get(dbid);
        }

        public Collection<CfgFolder> getAllFolders() {
            return Collections.unmodifiableCollection(byDbid.values());
        }

        public CfgFolder findBySegments(List<String> segments) {
            if (segments == null || segments.isEmpty()) return null;
            String key = String.join(" > ", segments);
            CfgFolder exact = byExactPath.get(key);
            if (exact != null) return exact;
            return byLowerPath.get(key.toLowerCase(Locale.ROOT));
        }

        public CfgFolder findChild(CfgFolder parent, String childName) {
            if (parent == null || childName == null) return null;
            List<CfgFolder> kids = childrenByParentDbid.get(parent.getDBID());
            if (kids == null) return null;
            for (CfgFolder k : kids) {
                if (childName.equals(k.getName())) return k;
            }
            for (CfgFolder k : kids) {
                if (childName.equalsIgnoreCase(k.getName())) return k;
            }
            return null;
        }

        public List<CfgFolder> getChildren(CfgFolder parent) {
            if (parent == null) return Collections.emptyList();
            List<CfgFolder> kids = childrenByParentDbid.get(parent.getDBID());
            return kids != null ? kids : Collections.emptyList();
        }

        public String resolvePath(CfgFolder folder) {
            if (folder == null) return "";
            List<String> segs = resolveSegments(folder);
            return String.join(" > ", segs);
        }

        public String resolveBackslashPath(Integer folderDbid) {
            CfgFolder f = getByDbid(folderDbid);
            if (f == null) return "";
            String base = f.getObjectPath();
            if (base == null || base.isEmpty() || "\\".equals(base)) {
                return "\\" + f.getName();
            }
            return base + "\\" + f.getName();
        }

        public String resolveDisplayPath(Integer folderDbid) {
            CfgFolder f = getByDbid(folderDbid);
            if (f == null) return "";
            return resolvePath(f);
        }

        public List<String> resolveSegments(CfgFolder folder) {
            if (folder == null) return Collections.emptyList();
            String objPath = folder.getObjectPath();
            if (objPath != null && !objPath.trim().isEmpty()) {
                List<String> segs = new ArrayList<>();
                for (String p : objPath.split("\\\\")) {
                    if (p != null && !p.trim().isEmpty()) {
                        segs.add(p.trim());
                    }
                }
                if (folder.getName() != null && !folder.getName().trim().isEmpty()) {
                    segs.add(folder.getName().trim());
                }
                return segs;
            }
            LinkedList<String> segs = new LinkedList<>();
            Set<Integer> seen = new HashSet<>();
            CfgFolder cur = folder;
            while (cur != null && seen.add(cur.getDBID())) {
                segs.addFirst(cur.getName());
                Integer pid = cur.getFolderId();
                cur = (pid != null && pid > 0) ? byDbid.get(pid) : null;
            }
            return segs;
        }
    }

    public FolderIndex loadIndex(IConfService service) throws Exception {
        CfgFolderQuery q = new CfgFolderQuery(service);
        Collection<CfgFolder> folders = q.execute();
        FolderIndex idx = new FolderIndex();
        if (folders == null) return idx;

        for (CfgFolder f : folders) {
            if (f.getDBID() != null) {
                idx.byDbid.put(f.getDBID(), f);
            }
        }
        for (CfgFolder f : folders) {
            Integer pid = f.getFolderId();
            if (pid != null && pid > 0) {
                idx.childrenByParentDbid.computeIfAbsent(pid, k -> new ArrayList<>()).add(f);
            }
            String fullKey = idx.resolvePath(f);
            if (!fullKey.isEmpty()) {
                idx.byExactPath.putIfAbsent(fullKey, f);
                idx.byLowerPath.putIfAbsent(fullKey.toLowerCase(Locale.ROOT), f);
            }
        }
        return idx;
    }

    public CfgFolder createSubFolder(IConfService service, CfgFolder parent, String name) throws Exception {
        if (parent == null) {
            throw new IllegalArgumentException("상위 폴더가 존재하지 않아 '" + name + "' 폴더를 생성할 수 없습니다.");
        }
        CfgFolder folder = new CfgFolder(service);
        folder.setName(name);
        folder.setType(parent.getType());
        if (parent.getOwnerID() != null) {
            CfgOwnerID owner = new CfgOwnerID(service, folder);
            owner.setDBID(parent.getOwnerID().getDBID());
            owner.setType(parent.getOwnerID().getType());
            folder.setOwnerID(owner);
        }
        folder.setFolderId(parent.getDBID());
        folder.setFolderClass(parent.getFolderClass() != null ? parent.getFolderClass() : CfgFolderClass.CFGFCDefault);
        folder.setCustomType(parent.getCustomType() != null ? parent.getCustomType() : 0);
        folder.save();
        return folder;
    }

    public List<String> makePath(List<String> base, List<String> sub) {
        if (sub == null || sub.isEmpty()) return null;
        List<String> arr = new ArrayList<>(base);
        for (String s : sub) {
            if (s != null && !s.trim().isEmpty()) {
                arr.add(s.trim());
            }
        }
        return arr;
    }

    public static class ResolvedFolder {
        public final CfgFolder folder;
        public final String displayPath;

        public ResolvedFolder(CfgFolder folder, String displayPath) {
            this.folder = folder;
            this.displayPath = displayPath;
        }
    }

    public ResolvedFolder resolvePersonsFolder(FolderIndex idx, CustomerRecord cust) {
        if (cust == null || cust.name == null) return null;
        String cName = cust.name.trim();
        List<String> base = Arrays.asList("Configuration", "Environment", "Persons");

        List<List<String>> candidates = new ArrayList<>();
        if (cName.contains("__TEST__")) {
            candidates.add(Collections.singletonList("TEST"));
        }
        if (PERSON_FOLDER_OVERRIDES.containsKey(cName)) {
            candidates.add(PERSON_FOLDER_OVERRIDES.get(cName));
        }
        if (cust.place != null && !cust.place.isEmpty()) {
            candidates.add(cust.place);
        }
        if (cust.agentLogin != null && !cust.agentLogin.isEmpty()) {
            candidates.add(cust.agentLogin);
        }
        if (cust.floor != null && !cust.floor.trim().isEmpty() && !"(위치 없음)".equals(cust.floor.trim())) {
            candidates.add(Arrays.asList(cust.floor.trim(), cName));
        }
        candidates.add(Collections.singletonList(cName));

        for (List<String> rel : candidates) {
            List<String> full = makePath(base, rel);
            CfgFolder found = idx.findBySegments(full);
            if (found != null) {
                return new ResolvedFolder(found, idx.resolvePath(found));
            }
        }

        // Fuzzy 2-level scan under Persons root
        CfgFolder pRoot = idx.findBySegments(base);
        if (pRoot != null) {
            String normTarget = normalizeFolderName(cName);
            for (CfgFolder lvl1 : idx.getChildren(pRoot)) {
                if (normalizeFolderName(lvl1.getName()).equals(normTarget)) {
                    return new ResolvedFolder(lvl1, idx.resolvePath(lvl1));
                }
                for (CfgFolder lvl2 : idx.getChildren(lvl1)) {
                    if (normalizeFolderName(lvl2.getName()).equals(normTarget)) {
                        return new ResolvedFolder(lvl2, idx.resolvePath(lvl2));
                    }
                }
            }
        }
        return null;
    }

    private static String normalizeFolderName(String raw) {
        if (raw == null) return "";
        return raw.replaceAll("[_\\s\\(\\)테스트전용폴더]", "").toLowerCase(Locale.ROOT);
    }

    public static String toDisplayPath(String backslashPath) {
        if (backslashPath == null || backslashPath.trim().isEmpty()) return "";
        List<String> parts = new ArrayList<>();
        for (String p : backslashPath.split("\\\\")) {
            if (p != null && !p.trim().isEmpty()) {
                parts.add(p.trim());
            }
        }
        return String.join(" > ", parts);
    }

    public Map<Integer, String> buildFolderDbidToCustomerMap(
            FolderIndex idx,
            List<CustomerRecord> customers,
            String switchName) {
        Map<Integer, String> map = new HashMap<>();
        if (idx == null || customers == null) return map;

        String sw = (switchName == null || switchName.trim().isEmpty()) ? "TServerAlcatel" : switchName.trim();
        List<String> baseLogin = Arrays.asList("Configuration", "Environment", "Switches", sw, "Agent Logins");
        List<String> baseAcd = Arrays.asList("Configuration", "Environment", "Switches", sw, "DNs", "ACDPosition");
        List<String> baseDn = Arrays.asList("Configuration", "Environment", "Switches", sw, "DNs", "AGENT_DN");
        List<String> baseRsi = Arrays.asList("Configuration", "Environment", "Switches", sw, "DNs", "RSI");
        List<String> basePlace = Arrays.asList("Configuration", "Environment", "Places", "AGENT");
        List<String> baseVQueue = Arrays.asList("Configuration", "Environment", "Switches", sw, "DNs", "VQueue");
        List<String> baseAgentGrp = Arrays.asList("Configuration", "Environment", "Agent Groups");
        List<String> baseDnGrp = Arrays.asList("Configuration", "Environment", "DN Groups");
        List<String> baseScripts = Arrays.asList("Configuration", "Environment", "Scripts", "유세스파트너스");

        for (CustomerRecord c : customers) {
            if (c == null || c.name == null || c.name.trim().isEmpty()) continue;
            String cName = c.name.trim();

            assignFolderSubtree(idx, idx.findBySegments(makePath(baseLogin, c.agentLogin)), cName, map);
            assignFolderSubtree(idx, idx.findBySegments(makePath(baseAcd, c.acdPosition)), cName, map);
            assignFolderSubtree(idx, idx.findBySegments(makePath(baseDn, c.agentDn)), cName, map);
            assignFolderSubtree(idx, idx.findBySegments(makePath(baseRsi, c.rsi)), cName, map);
            assignFolderSubtree(idx, idx.findBySegments(makePath(basePlace, c.place)), cName, map);

            ResolvedFolder pf = resolvePersonsFolder(idx, c);
            if (pf != null) {
                assignFolderSubtree(idx, pf.folder, cName, map);
            }

            assignFolderSubtree(idx, idx.findBySegments(makePath(baseVQueue, Collections.singletonList(cName))), cName, map);
            assignFolderSubtree(idx, idx.findBySegments(makePath(baseAgentGrp, Collections.singletonList(cName))), cName, map);
            assignFolderSubtree(idx, idx.findBySegments(makePath(baseDnGrp, Collections.singletonList(cName))), cName, map);
            assignFolderSubtree(idx, idx.findBySegments(makePath(baseScripts, Collections.singletonList(cName))), cName, map);
        }
        return map;
    }

    private static void assignFolderSubtree(
            FolderIndex idx,
            CfgFolder folder,
            String customerName,
            Map<Integer, String> out) {
        if (idx == null || folder == null || folder.getDBID() == null) return;
        out.putIfAbsent(folder.getDBID(), customerName);
        for (CfgFolder child : idx.getChildren(folder)) {
            assignFolderSubtree(idx, child, customerName, out);
        }
    }
}
