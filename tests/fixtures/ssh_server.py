"""Loopback-only SSH fixture. No real credentials or persisted host keys.

Install tests/fixtures/requirements.txt in an isolated venv. The C# test runner
starts this process and stops it by closing stdin. All listeners bind loopback.
"""
import json
import select
import socket
import sys
import threading
from pathlib import Path

import paramiko


def listener(port=0):
    sock = socket.socket()
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(("127.0.0.1", port))
    sock.listen(20)
    return sock


def bridge(left, right):
    try:
        while True:
            ready, _, _ = select.select([left, right], [], [], 1)
            for source in ready:
                data = source.recv(32768)
                if not data:
                    return
                (right if source is left else left).sendall(data)
    except (OSError, EOFError, paramiko.SSHException):
        pass
    finally:
        left.close()
        right.close()


host_key = paramiko.RSAKey.generate(2048)
user_key = paramiko.RSAKey.generate(2048)
key_path = Path(sys.argv[1]).resolve()
user_key.write_private_key_file(str(key_path), password="fixture-key-passphrase")
auto_key_path = key_path.with_suffix(".auto")
user_key.write_private_key_file(str(auto_key_path))


class Server(paramiko.ServerInterface):
    def __init__(self):
        self.transport = None
        self.destinations = {}
        self.listeners = {}

    def get_allowed_auths(self, username):
        return "password,publickey"

    def check_auth_password(self, username, password):
        return paramiko.AUTH_SUCCESSFUL if (username, password) == ("fixture", "fixture-password") else paramiko.AUTH_FAILED

    def check_auth_publickey(self, username, key):
        return paramiko.AUTH_SUCCESSFUL if username == "fixture" and key == user_key else paramiko.AUTH_FAILED

    def check_channel_direct_tcpip_request(self, chanid, origin, destination):
        if destination[0] != "127.0.0.1":
            return paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED
        self.destinations[chanid] = destination
        return paramiko.OPEN_SUCCEEDED

    def check_port_forward_request(self, address, port):
        if address != "127.0.0.1":
            return False
        try:
            sock = listener(port)
        except OSError:
            return False
        bound = sock.getsockname()[1]
        self.listeners[(address, bound)] = sock

        def accept_remote():
            while self.transport.is_active():
                try:
                    client, origin = sock.accept()
                    channel = self.transport.open_forwarded_tcpip_channel(origin, (address, bound))
                    threading.Thread(target=bridge, args=(client, channel), daemon=True).start()
                except (OSError, EOFError, paramiko.SSHException):
                    break
            sock.close()

        threading.Thread(target=accept_remote, daemon=True).start()
        return bound

    def cancel_port_forward_request(self, address, port):
        sock = self.listeners.pop((address, port), None)
        if sock:
            sock.close()


def handle_ssh(client):
    transport = paramiko.Transport(client)
    transport.add_server_key(host_key)
    server = Server()
    server.transport = transport
    try:
        transport.start_server(server=server)
        while transport.is_active():
            channel = transport.accept(0.3)
            if channel is None:
                continue
            destination = server.destinations.get(channel.get_id())
            if destination:
                try:
                    target = socket.create_connection(destination, timeout=3)
                    threading.Thread(target=bridge, args=(channel, target), daemon=True).start()
                except OSError:
                    channel.close()
    except (OSError, EOFError, paramiko.SSHException):
        pass
    finally:
        transport.close()
        for sock in list(server.listeners.values()):
            sock.close()


def accept_loop(sock, handler):
    while True:
        try:
            client, _ = sock.accept()
            threading.Thread(target=handler, args=(client,), daemon=True).start()
        except OSError:
            return


def echo(client):
    try:
        while data := client.recv(32768):
            client.sendall(data)
    except OSError:
        pass
    finally:
        client.close()


ssh = listener()
ssh_second = listener()
echo_socket = listener()
threading.Thread(target=accept_loop, args=(ssh, handle_ssh), daemon=True).start()
threading.Thread(target=accept_loop, args=(ssh_second, handle_ssh), daemon=True).start()
threading.Thread(target=accept_loop, args=(echo_socket, echo), daemon=True).start()
print(json.dumps({"sshPort": ssh.getsockname()[1], "sshPort2": ssh_second.getsockname()[1], "echoPort": echo_socket.getsockname()[1], "keyPath": str(key_path), "autoKeyPath": str(auto_key_path)}), flush=True)
sys.stdin.readline()
ssh.close()
ssh_second.close()
echo_socket.close()
