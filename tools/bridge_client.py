"""Authenticated loopback client and a manual shell; Python standard library only."""
from __future__ import annotations
import argparse
import itertools
import json
import os
from pathlib import Path
import shlex
import socket
import threading


class BridgeError(RuntimeError):
    def __init__(self, error):
        self.code = error.get("code", "error")
        super().__init__(f"{self.code}: {error.get('message', '')}")


def read_config(game_root: str):
    values = {}
    section = None
    file = Path(game_root) / "BepInEx/config/KK_StoryNPCBridge.cfg"
    for line in file.read_text(encoding="utf-8-sig").splitlines():
        if line.strip().startswith("["):
            section = line.strip()
        elif section == "[Bridge]" and "=" in line and not line.lstrip().startswith("#"):
            key, value = line.split("=", 1)
            values[key.strip()] = value.strip()
    return int(values["Port"]), values["Token"]


class BridgeClient:
    def __init__(self, game_root=None, port=None, token=None):
        game_root = game_root or os.environ.get("KOIKATU_GAME_ROOT")
        if token is None and not game_root:
            raise ValueError("Provide game_root or set KOIKATU_GAME_ROOT; alternatively supply an explicit token")
        configured_port, configured_token = read_config(game_root) if token is None else (port or 64321, token)
        self.socket = socket.create_connection(("127.0.0.1", port or configured_port), timeout=5)
        self.socket.settimeout(None)
        self.socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self._lock = threading.Lock()
        self._send_lock = threading.Lock()
        self._pending = {}
        self._counter = itertools.count(1)
        self._closed = threading.Event()
        self._closing = False
        self.last_snapshot = None
        self._reader = threading.Thread(target=self._read, daemon=True)
        self._reader.start()
        try:
            self.auth = self.request("auth", token=token or configured_token)
        except Exception:
            self.close()
            raise
        self._heartbeat = threading.Thread(target=self._ping, daemon=True)
        self._heartbeat.start()

    def _read(self):
        try:
            with self.socket.makefile("rb") as stream:
                while not self._closed.is_set():
                    line = stream.readline(2_000_001)
                    if not line or len(line) > 2_000_000:
                        break
                    message = json.loads(line)
                    if "event" in message:
                        if message["event"] == "snapshot":
                            self.last_snapshot = message["data"]
                        continue
                    with self._lock:
                        item = self._pending.get(message.get("id"))
                        if item:
                            item[1].append(message)
                            item[0].set()
        except (OSError, ValueError):
            pass
        finally:
            self._closed.set()
            with self._lock:
                for event, _ in self._pending.values():
                    event.set()

    def _ping(self):
        while not self._closed.wait(3):
            try:
                self.request("ping", timeout=4)
            except (OSError, RuntimeError, TimeoutError):
                self.close(graceful=False)
                return

    def request(self, op, timeout=7, **parameters):
        if self._closed.is_set():
            raise ConnectionError("Bridge connection is closed")
        identifier = f"r{next(self._counter)}"
        event, messages = threading.Event(), []
        with self._lock:
            self._pending[identifier] = (event, messages)
        try:
            frame = json.dumps({"id": identifier, "op": op, **parameters}, ensure_ascii=False, allow_nan=False).encode("utf-8") + b"\n"
            if len(frame) > 65536:
                raise ValueError("Command exceeds the bridge frame limit")
            with self._send_lock:
                self.socket.sendall(frame)
            if not event.wait(timeout):
                raise TimeoutError("No command result; inspect game state before retrying a mutation")
            if not messages:
                raise ConnectionError("Bridge disconnected")
            response = messages[0]
            if not response["ok"]:
                raise BridgeError(response["error"])
            return response["result"]
        finally:
            with self._lock:
                self._pending.pop(identifier, None)

    def close(self, graceful=True):
        if self._closing:
            return
        self._closing = True
        if graceful and not self._closed.is_set():
            try:
                self.request("disconnect", timeout=2)
            except (OSError, RuntimeError, TimeoutError):
                pass
        self._closed.set()
        try:
            self.socket.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass
        self.socket.close()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()


def show(value):
    print(json.dumps(value, ensure_ascii=False, indent=2))


def shell(client):
    print("Dedicated NPC: list | use 1 | get | report | player | time | stats | stat favor=20 relation=friend isStaff=true | face mouth=1")
    print("adjust MUTATION_ID favor=4 intimacy=-2 lewdness=4 (reuse the ID and deltas when retrying)")
    print("move x y z | warp x y z | turn degrees | stop | anim state | say text | dialogue text | reply text")
    print("release | events | quit. Closing the client releases temporary NPC control.")
    print("actions | action ID [map] [seconds] | queue ID [map] [seconds] | desire ID 0..100")
    print("summon (walk to player) | native_talk | say_native text | clear_queue")
    print("native_events | native_event KEY_OR_ID | waiting on/off")
    print("sequence FILE | sequence_status | sequence_cancel (ready native_talk required)")
    print("motion_catalog [FILE] | adv_motion FILE [seconds] (native ADV resource motion object)")
    print("dialogue text | invite [text] | reply text | respond text | dialogue_status | dialogue_retry | dialogue_close")
    target = None
    generation = None
    client.request("subscribe")
    while True:
        try:
            words = shlex.split(input("npc> "))
            if not words:
                continue
            command, *args = words
            if command in {"quit", "exit"}:
                return
            if command == "list":
                roster = client.request("list")
                for i, heroine in enumerate(roster["actors"], 1):
                    print(i, heroine["name"], heroine["id"], "loaded=" + str(heroine["loaded"]), "map=" + str(heroine["map"]))
            elif command == "use":
                roster = client.request("list")["actors"]
                chosen = roster[int(args[0]) - 1]
                if target:
                    client.request("release_all")
                claimed = client.request("claim", target=chosen["id"], generation=chosen["generation"])
                target = chosen["id"]
                generation = chosen["generation"]
                print("Selected", chosen["name"], target)
                print("Handover report:", claimed["autonomy"]["lastReportSummary"])
                show(claimed["activityReport"])
            elif command == "events":
                show((client.last_snapshot or client.request("status"))["events"])
            elif command == "report":
                show(client.request("status")["actors"][0]["activityReport"])
            elif command == "plan":
                show(client.request("native_plan", target="bridge:main"))
            elif command == "sample_action":
                show(client.request("sample_native_action"))
            elif command in {"player", "time"}:
                show(client.request("status")["player" if command == "player" else "gameTime"])
            elif command == "stats":
                show(client.request("stat_catalog"))
            elif command == "native_events":
                show(client.request("native_event_catalog"))
            elif command == "sequence_status":
                show(client.request("adv_sequence_status", target=target or "bridge:main"))
            elif command == "motion_catalog":
                motion = json.loads(Path(args[0]).read_text(encoding="utf-8-sig")) if args else {}
                show(client.request("adv_motion_catalog", target=target or "bridge:main", motion=motion))
            elif command in {"respond", "model"}:
                actor = client.request("status")["actors"][0]
                conversation = actor["dialogue"]
                show(client.request("dialogue_respond", target=actor["id"], generation=actor["generation"], conversationId=conversation["conversationId"], requestId=conversation["requestId"], text=" ".join(args)))
            elif command == "dialogue_status":
                show(client.request("status")["actors"][0]["dialogue"])
            elif not target:
                print("Select a heroine with use 1 first")
            elif command == "native_event":
                reference = int(args[0]) if args[0].isdigit() else args[0]
                show(client.request("trigger_native_event", target=target, generation=generation, event=reference))
            elif command == "sequence":
                segments = json.loads(Path(args[0]).read_text(encoding="utf-8-sig"))
                show(client.request("play_adv_sequence", target=target, generation=generation, segments=segments))
            elif command == "adv_motion":
                motion = json.loads(Path(args[0]).read_text(encoding="utf-8-sig"))
                seconds = float(args[1]) if len(args) > 1 else 3
                show(client.request("play_adv_motion", target=target, generation=generation, motion=motion, durationSeconds=seconds))
            elif command == "sequence_cancel":
                run = client.request("adv_sequence_status", target=target)
                if run is None:
                    raise ValueError("No ADV sequence exists")
                show(client.request("cancel_adv_sequence", target=target, generation=generation, sequenceId=run["sequenceId"]))
            elif command == "waiting":
                if args[0] not in {"on", "off"}:
                    raise ValueError("Use waiting on or waiting off")
                show(client.request("set_waiting_indicator", target=target, generation=generation, visible=args[0] == "on"))
            elif command == "adjust":
                if len(args) < 2:
                    raise ValueError("Use adjust MUTATION_ID favor=4 intimacy=-2 lewdness=4; reuse the ID and deltas when retrying")
                deltas = {}
                for arg in args[1:]:
                    key, value = arg.split("=", 1)
                    if key in deltas:
                        raise ValueError("Duplicate delta field: " + key)
                    deltas[key] = json.loads(value)
                show(client.request("adjust_stats", target=target, generation=generation, mutationId=args[0], deltas=deltas))
            elif command in {"stat", "face"}:
                changes = {}
                for arg in args:
                    key, value = arg.split("=", 1)
                    changes[key] = value if command == "stat" and key == "relation" and value in {"stranger", "acquaintance", "friend", "girlfriend"} else json.loads(value)
                show(client.request("set_stats" if command == "stat" else "set_expression", target=target, generation=generation, changes=changes))
            elif command in {"move", "warp"}:
                show(client.request("move" if command == "move" else "teleport", target=target, generation=generation, position=[float(x) for x in args]))
            elif command == "turn":
                show(client.request("turn", target=target, generation=generation, yaw=float(args[0])))
            elif command == "anim":
                show(client.request("play_animation", target=target, generation=generation, state=" ".join(args)))
            elif command == "actions":
                show(client.request("native_actions", target=target))
            elif command in {"action", "queue"}:
                options = {"action": int(args[0])}
                if len(args) > 1:
                    options["map"] = int(args[1])
                if len(args) > 2:
                    options["duration"] = float(args[2])
                show(client.request("set_action" if command == "action" else "queue_action", target=target, generation=generation, **options))
            elif command == "desire":
                show(client.request("set_desire", target=target, generation=generation, action=int(args[0]), value=int(args[1])))
            elif command == "say_native":
                show(client.request("say_native", target=target, generation=generation, text=" ".join(args)))
            elif command in {"say", "dialogue"}:
                show(client.request("say" if command == "say" else "dialogue_open", target=target, generation=generation, text=" ".join(args)))
            elif command == "invite":
                show(client.request("dialogue_open", target=target, generation=generation, initiator="npc", text=" ".join(args)))
            elif command == "reply":
                conversation = client.request("dialogue_status", target=target)
                show(client.request("dialogue_reply", target=target, generation=generation, conversationId=conversation["conversationId"], turn=conversation["turn"], text=" ".join(args)))
            elif command in {"dialogue_close", "dialogue_retry", "dialogue_request_input", "dialogue_wait", "dialogue_wait_model"}:
                conversation = client.request("dialogue_status", target=target)
                show(client.request(command, target=target, generation=generation, conversationId=conversation["conversationId"]))
            elif command in {"get", "stop", "release", "clear_expression", "summon", "native_talk", "clear_queue", "dialogue_status", "walk_to_player"}:
                show(client.request(command, target=target, generation=generation))
                if command == "release":
                    target = None
            else:
                print("Unknown command")
        except (EOFError, KeyboardInterrupt):
            return
        except (RuntimeError, OSError, ValueError, IndexError, TimeoutError) as error:
            print(error)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-root", default=os.environ.get("KOIKATU_GAME_ROOT"), help="Koikatu directory; defaults to KOIKATU_GAME_ROOT")
    parser.add_argument("--json", help="Send one command object, for example {\"op\":\"list\"}")
    options = parser.parse_args()
    if not options.game_root:
        parser.error("Provide --game-root or set KOIKATU_GAME_ROOT")
    with BridgeClient(options.game_root) as client:
        if options.json:
            data = json.loads(options.json)
            show(client.request(**data))
        else:
            shell(client)


if __name__ == "__main__":
    main()
