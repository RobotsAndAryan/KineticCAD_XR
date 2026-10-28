#!/usr/bin/env python3
"""
KineticCAD XR -> ROS 2 Jazzy & NVIDIA Isaac Sim Live UDP Bridge
Listens on UDP 0.0.0.0:9870 for 60 Hz JSON packets from Meta Quest 3 / Unity Editor:
  {"robot":"kinetic_claw_70mm","q":[0.25000,0.45000,-0.65000],"mu":1.240E-03}

Modes:
  1. ROS 2 Mode (automatic if `rclpy` is installed):
     Publishes `/joint_states` (sensor_msgs/msg/JointState) & `/cmd_vel` (geometry_msgs/msg/Twist).
  2. Standalone Telemetry Console Mode (automatic fallback on Windows / macOS / Linux without ROS 2):
     Renders a live high-contrast 60 Hz telemetry dashboard in the terminal for live demos and Isaac Sim socket piping.
"""

import argparse
import json
import math
import socket
import sys
import time

try:
    import rclpy
    from rclpy.node import Node
    from sensor_msgs.msg import JointState
    from geometry_msgs.msg import Twist
    HAS_ROS2 = True
except ImportError:
    HAS_ROS2 = False


JOINT_NAMES = [
    "base_yaw_joint",
    "shoulder_pitch_joint",
    "elbow_pitch_joint",
]


def run_ros2_node(udp_host: str, udp_port: int):
    class KineticCadUdpBridge(Node):
        def __init__(self):
            super().__init__("kinetic_cad_udp_bridge")
            self.joint_pub = self.create_publisher(JointState, "/joint_states", 10)
            self.twist_pub = self.create_publisher(Twist, "/cmd_vel", 10)

            self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.sock.bind((udp_host, udp_port))
            self.sock.setblocking(False)

            self.packet_count = 0
            self.timer = self.create_timer(1.0 / 120.0, self.poll_udp)
            self.get_logger().info(
                f"KineticCAD ROS 2 Bridge listening on UDP {udp_host}:{udp_port} -> /joint_states & /cmd_vel"
            )

        def poll_udp(self):
            latest_data = None
            while True:
                try:
                    data, _ = self.sock.recvfrom(4096)
                    latest_data = data
                except BlockingIOError:
                    break
                except Exception as exc:
                    self.get_logger().warn(f"UDP recv error: {exc}")
                    break

            if latest_data is None:
                return

            try:
                payload = json.loads(latest_data.decode("utf-8"))
                q = payload.get("q", [0.0, 0.0, 0.0])
                if len(q) < 3:
                    q = list(q) + [0.0] * (3 - len(q))

                now_msg = self.get_clock().now().to_msg()

                js = JointState()
                js.header.stamp = now_msg
                js.header.frame_id = "base_link"
                js.name = JOINT_NAMES
                js.position = [float(q[0]), float(q[1]), float(q[2])]
                self.joint_pub.publish(js)

                tw = Twist()
                tw.linear.x = float(q[1]) * 0.5
                tw.angular.z = float(q[0]) * 1.0
                self.twist_pub.publish(tw)

                self.packet_count += 1
                if self.packet_count % 60 == 1:
                    mu = float(payload.get("mu", 0.0))
                    robot = payload.get("robot", "kinetic_claw")
                    self.get_logger().info(
                        f"[{robot}] q={[round(float(x), 3) for x in q[:3]]} rad | mu={mu:.3e}"
                    )
            except Exception as exc:
                self.get_logger().warn(f"Failed to parse UDP JSON: {exc}")

    rclpy.init()
    node = KineticCadUdpBridge()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.sock.close()
        node.destroy_node()
        rclpy.shutdown()


def run_standalone_telemetry_console(udp_host: str, udp_port: int):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((udp_host, udp_port))
    sock.settimeout(0.25)

    print("=" * 88)
    print("  KINETIC-CAD XR | NVIDIA NEMOTRON x NEBIUS -> 60 Hz PHYSICAL AI TELEMETRY BRIDGE")
    print(f"  Listening on UDP {udp_host}:{udp_port}  (ROS 2 / NVIDIA Isaac Sim JointState Schema)")
    print("  Press Ctrl+C to stop.")
    print("=" * 88)

    packet_count = 0
    start_time = time.time()
    last_print = 0.0

    try:
        while True:
            try:
                data, addr = sock.recvfrom(4096)
            except socket.timeout:
                continue

            packet_count += 1
            now = time.time()
            if now - last_print < 0.05:
                continue
            last_print = now

            try:
                payload = json.loads(data.decode("utf-8"))
                robot = payload.get("robot", "kinetic_claw")
                q = payload.get("q", [0.0, 0.0, 0.0])
                mu = float(payload.get("mu", 0.0))
                deg = [math.degrees(float(rad)) for rad in q[:3]]
                elapsed = max(1e-3, now - start_time)
                hz = packet_count / elapsed
                health = "OPTIMAL (CYAN)" if mu > 4e-4 else "NEAR-SINGULAR (WARN)"

                sys.stdout.write(
                    f"\r[{addr[0]} | {hz:5.1f} Hz] Robot: {robot:<19} | "
                    f"J1:{deg[0]:+6.1f} deg  J2:{deg[1]:+6.1f} deg  J3:{deg[2]:+6.1f} deg | "
                    f"mu={mu:.2e} [{health}]   "
                )
                sys.stdout.flush()
            except Exception as exc:
                print(f"\n[WARN] Malformed packet: {exc}")
    except KeyboardInterrupt:
        print("\n[KineticCAD Telemetry Bridge] Stopped cleanly.")
    finally:
        sock.close()


def main():
    parser = argparse.ArgumentParser(description="KineticCAD XR 60 Hz UDP JointState Bridge")
    parser.add_argument("--host", default="0.0.0.0", help="UDP bind host (default: 0.0.0.0)")
    parser.add_argument("--port", type=int, default=9870, help="UDP bind port (default: 9870)")
    parser.add_argument("--standalone", action="store_true", help="Force standalone console mode even if rclpy is installed")
    args, unknown = parser.parse_known_args()

    # Support positional port argument e.g. `python udp_to_joint_states.py 9870`
    if unknown and unknown[0].isdigit():
        args.port = int(unknown[0])

    if HAS_ROS2 and not args.standalone:
        run_ros2_node(args.host, args.port)
    else:
        run_standalone_telemetry_console(args.host, args.port)


if __name__ == "__main__":
    main()
