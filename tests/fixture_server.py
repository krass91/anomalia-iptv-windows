"""Deterministic IPTV protocol fixtures. Binds only loopback; never proxies requests."""
import argparse
import gzip
import json
import pathlib
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass

    def reply(self, obj, status=200, content_type="application/json", headers=None):
        payload = obj if isinstance(obj, bytes) else json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(payload)

    def do_GET(self):
        u = urlsplit(self.path)
        q = {k: v[0] for k, v in parse_qs(u.query, keep_blank_values=True).items()}
        action = q.get("action", "")
        # Record protocol shape only, never username/password/token values.
        with self.server.lock:
            self.server.requests.append({"path": u.path, "action": action, "page": q.get("p", ""),
                                         "agent": self.headers.get("User-Agent", ""),
                                         "has_authorization": bool(self.headers.get("Authorization")),
                                         "has_cookie": bool(self.headers.get("Cookie"))})
        if u.path == "/shutdown":
            self.reply({"ok": True})
            threading.Thread(target=self.server.shutdown, daemon=True).start()
            return
        if u.path == "/requests":
            self.reply(self.server.requests)
            return
        if u.path == "/lists/playback.m3u":
            origin = "http://127.0.0.1:" + str(self.server.server_port)
            payload = ("#EXTM3U\n#EXTINF:-1 tvg-id=fixture.hls group-title=Локален тест,Синтетичен HLS тест\n" +
                       origin + "/media/hls/index.m3u8\n" +
                       "#EXTINF:-1 group-title=Локален тест,Синтетичен MPEG-TS тест\n" +
                       origin + "/media/hls/index0.ts\n")
            self.reply(payload.encode("utf-8"), content_type="audio/x-mpegurl; charset=utf-8")
            return
        if u.path.startswith("/media/hls/"):
            media_root = (pathlib.Path(__file__).parent / "fixtures" / "hls").resolve()
            media_path = (media_root / u.path.rsplit("/", 1)[-1]).resolve()
            if media_path.parent != media_root or not media_path.is_file():
                self.reply({"error": "fixture media not found"}, status=404)
            else:
                mime = "application/vnd.apple.mpegurl" if media_path.suffix == ".m3u8" else "video/mp2t"
                self.reply(media_path.read_bytes(), content_type=mime)
            return
        if u.path == "/lists/basic.m3u":
            payload = ('\ufeff#EXTM3U x-tvg-url="http://127.0.0.1/epg-fixture.xml"\n'
                       '#EXTINF:-1 tvg-id="bg.one" tvg-name="unused" tvg-logo="logo,one.png" group-title="Новини, България",Новини, 24 часа\n'
                       '#EXTVLCOPT:http-user-agent=Fixture channel agent\n'
                       '#EXTVLCOPT:http-referrer=http://127.0.0.1/reference\n'
                       'streams/one.m3u8\n'
                       '#EXTINF:-1 tvg-id=bg.two group-title=Спорт,Втори канал\n'
                       'http://127.0.0.1/stream/two.ts|User-Agent=Encoded%20Agent&Referer=http%3A%2F%2F127.0.0.1%2Fref\n')
            self.reply(payload.encode("utf-8"), content_type="audio/x-mpegurl; charset=utf-8")
            return
        if u.path == "/gzip-text":
            self.reply(gzip.compress("Кирилица gzip".encode("utf-8")), content_type="text/plain", headers={"Content-Encoding": "gzip"})
            return
        if u.path == "/oversize":
            self.reply(b"x" * 1025, content_type="application/octet-stream")
            return
        if u.path.endswith("/player_api.php"):
            if q.get("username") != "fixture user+ю" or q.get("password") != "fixture&pass=1":
                self.reply({"user_info": {"auth": 0, "status": "Disabled"}})
                return
            if not action:
                if u.path.startswith("/reject-auth/"):
                    info = {"auth": 0, "status": "Active"}
                elif u.path.startswith("/reject-expired/"):
                    info = {"auth": 1, "status": "Expired"}
                elif u.path.startswith("/reject-disabled/"):
                    info = {"auth": 1, "status": "Disabled"}
                elif u.path.startswith("/reject-missing/"):
                    self.reply({})
                    return
                else:
                    info = {"auth": 1, "status": "Active"}
                self.reply({"user_info": info})
                return
            if action.startswith("get_") and action.endswith("_categories"):
                self.reply([{"category_id": "7", "category_name": "Български " + action[4:-11]}])
                return
            data = {
                "get_live_streams": [{"stream_id": 101, "name": "Телевизия едно", "category_id": "7", "epg_channel_id": "bg.one", "stream_icon": "fixture-logo.png"}],
                "get_vod_streams": [{"stream_id": 202, "name": "Филм едно", "category_id": "7", "container_extension": "mkv"}],
                "get_series": [{"series_id": 303, "name": "Сериал едно", "category_id": "7"}],
                "get_series_info": {"episodes": {"2": [{"id": 302, "title": "Втори сезон", "container_extension": "mkv"}],
                                                  "1": [{"id": 301, "title": "Първи епизод"}]}}
            }
            self.reply(data.get(action, []))
            return
        if u.path.endswith("/portal.php"):
            portal = u.path.split("/")[1]
            if q.get("JsHttpRequest") != "1-xml" or not self.headers.get("Cookie", "").startswith("mac=") or self.headers.get("X-User-Agent") != "Model: MAG254; Link: Ethernet":
                self.reply({"error": "missing portal protocol headers"}, status=400)
                return
            if action == "handshake":
                self.reply({"js": {"token": "fixture-token-" + portal}})
                return
            if self.headers.get("Authorization") != "Bearer fixture-token-" + portal:
                self.reply({"error": "wrong portal fixture token"}, status=403)
                return
            if action == "get_profile":
                self.reply({"js": {"status": 1}})
                return
            if action == "get_genres":
                self.reply({"js": [{"id": "10", "title": "Новини"}]})
                return
            if action == "get_ordered_list":
                page = int(q.get("p", "1"))
                def channel(n):
                    return {"id": str(n), "name": portal + " канал " + str(n), "cmd": "ffmpeg http://localhost/ch/" + str(n), "tv_genre_id": "10", "xmltv_id": "bg." + str(n)}
                rows = [channel(1), channel(2)] if page == 1 else [channel(2), channel(3)] if page == 2 else []
                self.reply({"js": {"data": rows, "total_items": 3, "max_page_items": 2}})
                return
            if action == "create_link":
                if not q.get("cmd", "").startswith("ffmpeg http://localhost/ch/"):
                    self.reply({"error": "cmd did not survive query encoding"}, status=400)
                else:
                    self.reply({"js": {"cmd": "ffmpeg http://127.0.0.1:" + str(self.server.server_port) + "/resolved/" + portal + "/stream.ts"}})
                return
            self.reply({"js": {}})
            return
        self.reply({"error": "unknown local fixture"}, status=404)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--state", required=True)
    p.add_argument("--requests", required=True)
    args = p.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    server.requests, server.lock = [], threading.Lock()
    pathlib.Path(args.state).write_text(json.dumps({"base_url": "http://127.0.0.1:" + str(server.server_port)}), encoding="utf-8")
    try:
        server.serve_forever(poll_interval=0.1)
    finally:
        pathlib.Path(args.requests).write_text(json.dumps(server.requests, ensure_ascii=False, indent=2), encoding="utf-8")
        server.server_close()


if __name__ == "__main__":
    main()
