package com.gcti.extbatch.psdk;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.File;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;

@Service
public class CustomerCatalogService {
    private static final Logger log = LoggerFactory.getLogger(CustomerCatalogService.class);

    @JsonIgnoreProperties(ignoreUnknown = true)
    public static class CustomerRecord {
        public String name;
        public String floor;
        public List<String> agentLogin;
        public List<String> acdPosition;
        public List<String> agentDn;
        public List<String> place;
        public List<String> rsi;
        public Boolean useVQueue;
        public Boolean useIvr;
        public String note;
    }

    private final ObjectMapper mapper;
    private final String configuredPath;
    private long lastLoadedMtime = -1L;
    private List<CustomerRecord> cached = Collections.emptyList();

    public CustomerCatalogService(@Value("${extbatch.customers-json-path:../cm_customers.json}") String configuredPath) {
        this.configuredPath = configuredPath;
        this.mapper = new ObjectMapper();
        this.mapper.configure(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES, false);
    }

    private File resolveFile() {
        List<File> candidates = new ArrayList<>();
        if (configuredPath != null && !configuredPath.trim().isEmpty()) {
            candidates.add(new File(configuredPath.trim()));
        }
        candidates.add(new File("cm_customers.json"));
        candidates.add(new File("../cm_customers.json"));
        candidates.add(new File("C:/GCTI/ExtBatch/cm_customers.json"));
        for (File f : candidates) {
            if (f.exists() && f.isFile()) {
                return f;
            }
        }
        return candidates.get(0);
    }

    public synchronized List<CustomerRecord> loadAll() {
        File f = resolveFile();
        if (!f.exists() || !f.isFile()) {
            throw new IllegalStateException("고객사 매핑 파일(cm_customers.json)을 찾을 수 없습니다: " + f.getAbsolutePath());
        }
        long mtime = f.lastModified();
        if (cached.isEmpty() || mtime != lastLoadedMtime) {
            try {
                List<CustomerRecord> list = mapper.readValue(f, new TypeReference<List<CustomerRecord>>() {});
                this.cached = list != null ? list : Collections.emptyList();
                this.lastLoadedMtime = mtime;
                log.debug("Loaded {} customers from {}", this.cached.size(), f.getAbsolutePath());
            } catch (Exception ex) {
                throw new IllegalStateException("cm_customers.json 로드 실패: " + ex.getMessage(), ex);
            }
        }
        return cached;
    }

    public CustomerRecord findByName(String name) {
        if (name == null || name.trim().isEmpty()) return null;
        String target = name.trim();
        for (CustomerRecord c : loadAll()) {
            if (c.name != null && c.name.trim().equals(target)) {
                return c;
            }
        }
        for (CustomerRecord c : loadAll()) {
            if (c.name != null && c.name.trim().equalsIgnoreCase(target)) {
                return c;
            }
        }
        return null;
    }
}
