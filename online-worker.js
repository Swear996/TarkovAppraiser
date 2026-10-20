// Tarkov Appraiser - online counter (Cloudflare Worker + D1)
//
// The program sends an anonymous signal every 10 minutes: only a random ID that is created once
// on the PC (no name, no IP stored, no game data). Who sent one in the last 15 minutes counts as online.
//
//   POST /ping    {"id":"<32 hex chars>"}  -> {"online": 12}
//   GET  /online                            -> {"online": 12}        (for the website)
//   GET  /badge                             -> shields.io badge data (for the README)
//
// Needs a D1 database bound as "DB" with this table (run once in the D1 console):
//   CREATE TABLE IF NOT EXISTS online (id TEXT PRIMARY KEY, seen INTEGER NOT NULL);
//   CREATE INDEX IF NOT EXISTS online_seen ON online(seen);

const WINDOW = 15 * 60;          // seconds: counted as online after the last signal
const COUNT_CACHE = 60;          // seconds: the number is counted at most once a minute per server

let cached = { n: 0, at: 0 };

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type",
};

function json(data, status = 200, extra = {}) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "Content-Type": "application/json", ...CORS, ...extra },
  });
}

async function countOnline(env, ctx, now) {
  if (now - cached.at < COUNT_CACHE) return cached.n;
  const row = await env.DB.prepare("SELECT COUNT(*) AS n FROM online WHERE seen > ?1")
    .bind(now - WINDOW).first();
  cached = { n: row ? row.n : 0, at: now };
  // now and then: forget IDs that were not seen for a day
  if (Math.random() < 0.02)
    ctx.waitUntil(env.DB.prepare("DELETE FROM online WHERE seen < ?1").bind(now - 86400).run());
  return cached.n;
}

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    const now = Math.floor(Date.now() / 1000);

    if (request.method === "OPTIONS") return new Response(null, { headers: CORS });

    if (url.pathname === "/ping" && request.method === "POST") {
      let id = "";
      try { id = String((await request.json()).id || ""); } catch { }
      if (!/^[a-f0-9]{32}$/.test(id)) return json({ error: "bad id" }, 400);
      await env.DB.prepare(
        "INSERT INTO online (id, seen) VALUES (?1, ?2) ON CONFLICT(id) DO UPDATE SET seen = ?2"
      ).bind(id, now).run();
      return json({ online: await countOnline(env, ctx, now) });
    }

    if (url.pathname === "/online")
      return json({ online: await countOnline(env, ctx, now) }, 200, { "Cache-Control": "public, max-age=60" });

    if (url.pathname === "/badge") {
      const n = await countOnline(env, ctx, now);
      return json({ schemaVersion: 1, label: "online now", message: String(n), color: "5b8cff" },
        200, { "Cache-Control": "public, max-age=60" });
    }

    return new Response("Tarkov Appraiser online counter", { headers: CORS });
  },
};
