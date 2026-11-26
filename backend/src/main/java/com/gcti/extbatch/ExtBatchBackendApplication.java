package com.gcti.extbatch;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.context.properties.EnableConfigurationProperties;
import com.gcti.extbatch.config.BackendProperties;

@SpringBootApplication
@EnableConfigurationProperties(BackendProperties.class)
public class ExtBatchBackendApplication {
    public static void main(String[] args) {
        SpringApplication.run(ExtBatchBackendApplication.class, args);
    }
}
