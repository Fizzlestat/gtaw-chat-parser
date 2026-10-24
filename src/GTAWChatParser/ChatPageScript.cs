namespace GTAWChatParser;

internal static class ChatPageScript
{
    public const string ExtractLines = """
        (() => {
          const timePattern = /\b\d{1,2}:\d{2}:\d{2}\b/;
          const lines = [];
          const rows = document.querySelectorAll(".chat__messages > li");

          const rgbOf = (value) => {
            const match = String(value || "").match(/rgba?\(\s*(\d+)[,\s]+(\d+)[,\s]+(\d+)(?:[,\s/]+([0-9.]+))?/i);
            if (!match) return null;
            if (match[4] !== undefined && Number(match[4]) === 0) return null;
            return [Number(match[1]), Number(match[2]), Number(match[3])];
          };

          const codeFor = (rgb) => "^#" + rgb.map((part) => part.toString(16).padStart(2, "0")).join("").toUpperCase();

          const collapse = (value) => String(value || "").replace(/\s+/g, " ").trim();

          const withCodes = (row) => {
            let text = "";
            let last = "";
            const emit = (key) => {
              if (!key || key === last) return;
              text += codeFor(key.split(",").map(Number));
              last = key;
            };
            const visit = (node, parentKey) => {
              if (node.nodeType === 3) {
                const value = node.textContent || "";
                if (!value) return;
                emit(parentKey);
                text += value;
                return;
              }

              if (node.nodeType !== 1) return;
              const style = getComputedStyle(node);
              if (style.display === "none" || style.visibility === "hidden") return;

              const rgb = rgbOf(style.color);
              const key = rgb ? rgb.join(",") : parentKey;
              for (const child of node.childNodes) visit(child, key);
            };

            visit(row, "");
            return collapse(text);
          };

          for (const row of rows) {
            const text = collapse(row.innerText);
            if (!text) continue;

            let timestamp = "";
            const nodes = [row].concat(Array.from(row.querySelectorAll("*")));
            for (const node of nodes) {
              const attributes = node.attributes ? Array.from(node.attributes) : [];
              for (const attribute of attributes) {
                const match = String(attribute.value).match(timePattern);
                if (match) {
                  timestamp = match[0];
                  break;
                }
              }

              if (!timestamp) {
                const before = String(getComputedStyle(node, "::before").content || "");
                const match = before.match(timePattern);
                if (match) timestamp = match[0];
              }

              if (timestamp) break;
            }

            const prefix = timestamp ? "[" + timestamp + "] " : "";
            const colored = withCodes(row);
            lines.push({
              plain: prefix + text,
              colored: prefix + (colored || text)
            });
          }

          return JSON.stringify(lines);
        })()
        """;
}
