package com.gcti.extbatch.service;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public final class NumberSpecParser {
    private static final Pattern RANGE_PATTERN = Pattern.compile("^(\\d+)[~\\-](\\d+)$");
    private static final Pattern DIGITS_PATTERN = Pattern.compile("^\\d+$");

    private NumberSpecParser() {}

    public static List<String> expand(String spec) {
        if (spec == null || spec.trim().isEmpty()) {
            return new ArrayList<>();
        }
        Set<String> unique = new LinkedHashSet<>();
        String[] tokens = spec.split("[,;\\r\\n\\s\\t]+");
        for (String rawTok : tokens) {
            if (rawTok == null) continue;
            String t = rawTok.trim();
            if (t.isEmpty()) continue;

            Matcher rm = RANGE_PATTERN.matcher(t);
            if (rm.matches()) {
                String sStr = rm.group(1);
                String eStr = rm.group(2);
                long sNum = Long.parseLong(sStr);
                long eNum = Long.parseLong(eStr);
                if (eNum < sNum) {
                    throw new IllegalArgumentException("범위 끝 번호가 시작 번호보다 작습니다: " + t);
                }
                if ((eNum - sNum + 1) > 500) {
                    throw new IllegalArgumentException("한 번에 500건을 초과하는 범위는 허용되지 않습니다: " + t);
                }
                int width = sStr.length();
                for (long n = sNum; n <= eNum; n++) {
                    unique.add(padLeftZero(n, width));
                }
            } else if (DIGITS_PATTERN.matcher(t).matches()) {
                unique.add(t);
            } else {
                throw new IllegalArgumentException("올바른 숫자 또는 범위 형식이 아닙니다: '" + t + "' (예: 31001 또는 31001~31010)");
            }
        }
        return new ArrayList<>(unique);
    }

    private static String padLeftZero(long num, int width) {
        String s = Long.toString(num);
        if (s.length() >= width) return s;
        StringBuilder sb = new StringBuilder(width);
        for (int i = s.length(); i < width; i++) sb.append('0');
        sb.append(s);
        return sb.toString();
    }
}
