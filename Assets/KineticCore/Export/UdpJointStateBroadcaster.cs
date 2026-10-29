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
        public string TargetIp = "192.168.1.80"; // Local Host PC IP
        public int TargetPort = 9870;
        public float BroadcastHz = 60.0f;

        private UdpClient _udpClient;
        private float _accumulator = 0f;
        private readonly string[] _broadcastDestinations = new string[]
        {
            "192.168.1.80",
            "192.168.1.255",
            "255.255.255.255",
            "127.0.0.1",
            "192.168.1.131"
        };

        private void Start()
        {
            _udpClient = new UdpClient();
            try { _udpClient.EnableBroadcast = true; } catch { }

            if (Controller == null)
                Controller = GetComponent<KineticMechanismController>();

            // 1. Check Resources/target_ip.txt (bundled into Quest APK)
            try
            {
                TextAsset resIp = Resources.Load<TextAsset>("target_ip");
                if (resIp != null && !string.IsNullOrEmpty(resIp.text))
                {
                    string trimmed = resIp.text.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) TargetIp = trimmed;
                }
            }
            catch { }

            // 2. Allow overriding TargetIp on Quest 3 via persistentDataPath/target_ip.txt
            string ipFile = Path.Combine(Application.persistentDataPath, "target_ip.txt");
            if (File.Exists(ipFile))
            {
                string customIp = File.ReadAllText(ipFile).Trim();
                if (!string.IsNullOrEmpty(customIp))
                    TargetIp = customIp;
            }
            Debug.Log($"[KineticCAD UDP] Streaming 60Hz joint states to {TargetIp}:{TargetPort} + Subnet Broadcast");
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
            double mu = Controller.CurrentManipulability;
            if (double.IsNaN(mu) || double.IsInfinity(mu) || mu <= 0.0)
                mu = 1e-4;

            CultureInfo inv = CultureInfo.InvariantCulture;
            string payload = string.Format(
                inv,
                "{{\"robot\":\"{0}\",\"q\":[{1:F5},{2:F5},{3:F5}],\"mu\":{4:F6}}}",
                Controller.CurrentSpec != null ? Controller.CurrentSpec.robotName : "kinetic_claw",
                q.Length > 0 ? q[0] : 0.0,
                q.Length > 1 ? q[1] : 0.0,
                q.Length > 2 ? q[2] : 0.0,
                mu
            );

            byte[] bytes = Encoding.UTF8.GetBytes(payload);

            // Send to TargetIp first
            try { _udpClient.Send(bytes, bytes.Length, TargetIp, TargetPort); } catch { }

            // Also broadcast so any PC on the LAN running the Python bridge receives telemetry without IP configuration
            for (int i = 0; i < _broadcastDestinations.Length; i++)
            {
                string dest = _broadcastDestinations[i];
                if (dest != TargetIp)
                {
                    try { _udpClient.Send(bytes, bytes.Length, dest, TargetPort); } catch { }
                }
            }
        }

        private void OnDestroy()
        {
            _udpClient?.Close();
        }
    }
}
