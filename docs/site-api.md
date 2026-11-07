# How the mod talks to optcgtrainer.com

The mod never sees your cookies or your password. You link the sim once, like a TV, and the site gives the mod a
**device token** that can do only three things: say who you are, upload a game log, add a private note to your own games.

## Linking (device flow)
1. The mod asks for a code: `POST /api/device/start` `{ "name": "OPTCGSim on <PC name>" }`
   → `{ deviceCode, userCode: "ABCD-EFGH", expiresIn: 600, interval: 2 }`. No cookies, no token.
2. The mod opens `https://optcgtrainer.com/?link=ABCD-EFGH` in your browser and shows the same code in the sim.
   (The mod builds this address itself: it doesn't open addresses sent by the server.)
3. On the site, signed in, you see the code, how long ago it was requested and, if it was requested from another network,
   a warning ("if someone sent you this link, don't link"). **Link** → `POST /api/device/confirm`.
4. Meanwhile the mod polls `POST /api/device/poll { deviceCode }` → `pending`, then **once** `{ status: "linked", token }`.
   Codes expire after 10 minutes; requests are rate-limited.

## Using the token
`Authorization: Bearer <token>` (with it the server ignores cookies). Allowed, everything else is refused:
- `GET /api/me`
- `POST /api/replays` — body: the game log as OPTCGSim wrote it; headers `X-File-Name`, `X-Played-At`
- `POST /api/replays/:id/notes` — `{ line, body }`, only on your own games, only private notes

`line` is how many lines OPTCGSim had written to the log when you opened the note: on the site the note goes to the
moment of the game at that line. The token is stored in `%LOCALAPPDATA%\OPTCGTrainer\account.json`, it's sent only to the
site in the mod settings, never through a redirect, and you can remove it any time from Settings → *OPTCGSim linked*.
