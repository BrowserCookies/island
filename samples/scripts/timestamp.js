// Island example (Node.js): put a timestamp on the clipboard, optionally open a URL.
//
// Button setup:  Action = Run script, Script file = this file,
//                Output handling = JsonProtocol
//                (optional) Arguments = https://time.is
//
// The LAST non-empty line of stdout is parsed as JSON.

const now = new Date();
const stamp = now.toISOString().replace("T", " ").slice(0, 19) + "Z";
const url = process.argv[2];

const reply = {
  notify: `Copied ${stamp}`,
  clipboard: stamp,
};
if (url) reply.openUrl = url;

console.log(JSON.stringify(reply));
