namespace GTAWChatParser;

internal static class ChatPageScript
{
    /// <summary>
    /// Reads GTA World's visible chat rows in document order.
    /// Times painted beside a row, including CSS ::before text, are kept as a [H:mm:ss] prefix.
    /// </summary>
    public const string ExtractLines = """
        (() => {
          const timePattern = /\b\d{1,2}:\d{2}:\d{2}\b/;
          const lines = [];
          const rows = document.querySelectorAll(".chat__messages > li");

          for (const row of rows) {
            const text = (row.innerText || "").replace(/\s+/g, " ").trim();
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

            lines.push((timestamp ? "[" + timestamp + "] " : "") + text);
          }

          return JSON.stringify(lines);
        })()
        """;
}
