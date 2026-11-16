package club.njitmcl.agent;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 极简 JSON 解析器。
 *
 * 代理不能带第三方库（jar 要能被塞进任何服务端进程），而 MUA 的 hasJoined
 * 响应结构固定，所以写一个够用的递归下降解析就够：对象、数组、字符串、数字、布尔、null。
 * 不做 JS 那套全部转义兼容，只处理 JSON 规范内的转义。
 */
final class Json {

    private final String text;
    private int pos;

    private Json(String text) {
        this.text = text;
    }

    static Object parse(String text) {
        if (text == null) {
            return null;
        }
        return new Json(text).readValue();
    }

    private void skipWhitespace() {
        while (pos < text.length() && Character.isWhitespace(text.charAt(pos))) {
            pos++;
        }
    }

    private Object readValue() {
        skipWhitespace();
        if (pos >= text.length()) {
            return null;
        }
        char c = text.charAt(pos);
        switch (c) {
            case '{':
                return readObject();
            case '[':
                return readArray();
            case '"':
                return readString();
            case 't':
                pos += 4;
                return Boolean.TRUE;
            case 'f':
                pos += 5;
                return Boolean.FALSE;
            case 'n':
                pos += 4;
                return null;
            default:
                return readNumber();
        }
    }

    private Map<String, Object> readObject() {
        Map<String, Object> result = new LinkedHashMap<String, Object>();
        pos++; // {
        skipWhitespace();
        if (pos < text.length() && text.charAt(pos) == '}') {
            pos++;
            return result;
        }
        while (pos < text.length()) {
            skipWhitespace();
            String key = readString();
            if (key == null) {
                break;
            }
            skipWhitespace();
            if (pos < text.length() && text.charAt(pos) == ':') {
                pos++;
            }
            result.put(key, readValue());
            skipWhitespace();
            if (pos < text.length() && text.charAt(pos) == ',') {
                pos++;
                continue;
            }
            if (pos < text.length() && text.charAt(pos) == '}') {
                pos++;
            }
            break;
        }
        return result;
    }

    private List<Object> readArray() {
        List<Object> result = new ArrayList<Object>();
        pos++; // [
        skipWhitespace();
        if (pos < text.length() && text.charAt(pos) == ']') {
            pos++;
            return result;
        }
        while (pos < text.length()) {
            result.add(readValue());
            skipWhitespace();
            if (pos < text.length() && text.charAt(pos) == ',') {
                pos++;
                continue;
            }
            if (pos < text.length() && text.charAt(pos) == ']') {
                pos++;
            }
            break;
        }
        return result;
    }

    private String readString() {
        if (pos >= text.length() || text.charAt(pos) != '"') {
            return null;
        }
        pos++;
        StringBuilder builder = new StringBuilder();
        while (pos < text.length()) {
            char c = text.charAt(pos++);
            if (c == '"') {
                return builder.toString();
            }
            if (c != '\\') {
                builder.append(c);
                continue;
            }
            if (pos >= text.length()) {
                break;
            }
            char esc = text.charAt(pos++);
            switch (esc) {
                case '"':
                    builder.append('"');
                    break;
                case '\\':
                    builder.append('\\');
                    break;
                case '/':
                    builder.append('/');
                    break;
                case 'b':
                    builder.append('\b');
                    break;
                case 'f':
                    builder.append('\f');
                    break;
                case 'n':
                    builder.append('\n');
                    break;
                case 'r':
                    builder.append('\r');
                    break;
                case 't':
                    builder.append('\t');
                    break;
                case 'u':
                    if (pos + 4 <= text.length()) {
                        builder.append((char) Integer.parseInt(text.substring(pos, pos + 4), 16));
                        pos += 4;
                    }
                    break;
                default:
                    builder.append(esc);
            }
        }
        return builder.toString();
    }

    private Object readNumber() {
        int start = pos;
        while (pos < text.length()) {
            char c = text.charAt(pos);
            if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') {
                pos++;
            } else {
                break;
            }
        }
        if (start == pos) {
            pos++;
            return null;
        }
        try {
            return Double.valueOf(text.substring(start, pos));
        } catch (NumberFormatException ex) {
            return null;
        }
    }
}
