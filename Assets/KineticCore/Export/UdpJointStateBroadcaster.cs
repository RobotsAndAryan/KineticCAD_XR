using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;
using KineticCore.Runtime;

namespace KineticCore.Export
{
    [Preserve]
    public class UdpJointStateBroadcaster : MonoBehaviour
    {
        public KineticMechanismController Controller;
        public string TargetIp = "192.168.1.131"; // Ubuntu ROS 2 Jazzy Laptop IP
        public int TargetPort = 9870;
        public float BroadcastHz = 60.0f;

        private UdpClient _udpClient;
        private float _accumulator = 0f;

        private void Start()
        {
            _udpClient = new UdpClient();
            if (Controller == null)
                Controller = GetComponent<KineticMechanismController>();

            // Allow overriding TargetIp on Quest 3 via persistentDataPath/target_ip.txt
            string ipFile = Path.Combine(Application.persistentDataPath, "target_ip.txt");
            if (File.Exists(ipFile))
            {
                string customIp = File.ReadAllText(ipFile).Trim();
                if (!string.IsNullOrEmpty(customIp))
                    TargetIp = customIp;
            }
            Debug.Log($"[KineticCAD UDP] Streaming 60Hz joint states to {TargetIp}:{TargetPort}");
        }

        private void Update()
        {
            if (Controller == null || Controller.JointAngles == null) return;

            float interval = 1.0f / Mathf.Max(1.0f, BroadcastHz);
            _accumulator += Time.unscaledDeltaTime;
            if (_accumulator < interval) return;
            _accumulator -= interval;
            if (_accumulator > interval) _accumulator = 0f;

            double[] q = Controller.JointAngles;
            CultureInfo inv = CultureInfo.InvariantCulture;
            string payload = string.Format(
                inv,
                "{{\"robot\":\"{0}\",\"q\":[{1:F5},{2:F5},{3:F5}],\"mu\":{4:E3}}}",
                Controller.CurrentSpec != null ? Controller.CurrentSpec.robotName : "kinetic_claw",
                q.Length > 0 ? q[0] : 0.0,
                q.Length > 1 ? q[1] : 0.0,
                q.Length > 2 ? q[2] : 0.0,
                Controller.CurrentManipulability
            );

            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            try
            {
                _udpClient.Send(bytes, bytes.Length, TargetIp, TargetPort);
                if (TargetIp != "127.0.0.1" && TargetIp != "localhost")
                {
                    _udpClient.Send(bytes, bytes.Length, "127.0.0.1", TargetPort);
                }
            }
            catch
            {
            }
        }

        private void OnDestroy()
        {
            _udpClient?.Close();
        }
    }
}
