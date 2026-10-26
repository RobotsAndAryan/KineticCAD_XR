#!/usr/bin/env python3
"""
KineticCAD XR -> ROS 2 Jazzy & NVIDIA Isaac Sim Live UDP Bridge
Listens on UDP 0.0.0.0:9870 for 60 Hz JSON packets from Meta Quest 3 / Unity Editor:
  {"robot":"kinetic_claw_70mm","q":[0.25000,0.45000,-0.65000],"mu":1.240E-03}
Publishes:
  - /joint_states (sensor_msgs/msg/JointState) with names:
      ['base_yaw_joint', 'shoulder_pitch_joint', 'elbow_pitch_joint']
  - /cmd_vel (geometry_msgs/msg/Twist) for mobile/holonomic or differential base testing
"""

import json
import socket
import sys

import rclpy
from rclpy.node import Node
from sensor_msgs.msg import JointState
from geometry_msgs.msg import Twist


class KineticCadUdpBridge(Node):
    def __init__(self, udp_host: str = "0.0.0.0", udp_port: int = 9870):
        super().__init__("kinetic_cad_udp_bridge")
        self.joint_pub = self.create_publisher(JointState, "/joint_states", 10)
        self.twist_pub = self.create_publisher(Twist, "/cmd_vel", 10)

        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind((udp_host, udp_port))
        self.sock.setblocking(False)

        self.joint_names = [
            "base_yaw_joint",
            "shoulder_pitch_joint",
            "elbow_pitch_joint",
        ]
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
            js.name = self.joint_names
            js.position = [float(q[0]), float(q[1]), float(q[2])]
            self.joint_pub.publish(js)

            tw = Twist()
            tw.linear.x = float(q[1]) * 0.5
            tw.angular.z = float(q[0]) * 1.0
            self.twist_pub.publish(tw)

            self.packet_count += 1
            if self.packet_count % 120 == 1:
                mu = payload.get("mu", 0.0)
                robot = payload.get("robot", "kinetic_claw")
                self.get_logger().info(
                    f"[{robot}] q={[round(float(x), 3) for x in q[:3]]} rad | mu={mu:.3e}"
                )
        except Exception as exc:
            self.get_logger().warn(f"Failed to parse UDP JSON: {exc}")


def main(args=None):
    rclpy.init(args=args)
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 9870
    node = KineticCadUdpBridge(udp_port=port)
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.sock.close()
        node.destroy_node()
        rclpy.shutdown()


if __name__ == "__main__":
    main()
