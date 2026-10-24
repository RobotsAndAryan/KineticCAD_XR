using System.Globalization;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using KineticCore.Runtime;

namespace KineticCore.Export
{
    public class UdpJointStateBroadcaster : MonoBehaviour
    {
        public KineticMechanismController Controller;
        public string TargetIp = "192.168.1.131";
        public int TargetPort = 9870;

        private UdpClient _udpClient;

        private void Start()
        {
            _udpClient = new UdpClient();
            if (Controller == null)
                Controller = GetComponent<KineticMechanismController>();
        }

        private void Update()
        {
            if (Controller == null || Controller.JointAngles == null) return;

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