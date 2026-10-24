using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using KineticCore.Schema;

namespace KineticCore.AI
{
    public class NebiusKinematicSynthesizer
    {
        private const string EndpointUrl = "https://api.tokenfactory.nebius.com/v1/chat/completions";
        private readonly string _apiKey;
        private readonly string _modelId;

        public NebiusKinematicSynthesizer(
            string apiKey = null,
            string modelId = "nvidia/Llama-3_3-Nemotron-Super-49B-v1")
        {
            _apiKey = string.IsNullOrEmpty(apiKey)
                ? Environment.GetEnvironmentVariable("NEBIUS_API_KEY")
                : apiKey;
            _modelId = modelId;
        }

        public async Task<MechanismSpec> SynthesizeMechanismAsync(double caliperWidthMm, string userIntent)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                Debug.LogWarning("[NebiusSynthesizer] NEBIUS_API_KEY not set. Using deterministic parametric synthesis.");
                return BuildDeterministicMechanism(caliperWidthMm);
            }

            string prompt =
                $"Design a 3-DoF spatial robotic mechanism sized for an object of width {caliperWidthMm:F1} mm. " +
                $"User intent: {userIntent}. Return ONLY valid JSON matching MechanismSpec with fields: " +
                $"robotName, targetCaliperWidthMm, links[], joints[], endEffectorHomePosition[3].";

            string requestBody = BuildChatCompletionPayload(prompt);

            using (UnityWebRequest req = new UnityWebRequest(EndpointUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                req.timeout = 8;

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string rawResponse = req.downloadHandler.text;
                    string extractedJson = ExtractInnerJsonObject(rawResponse);
                    if (!string.IsNullOrEmpty(extractedJson))
                    {
                        try
                        {
                            MechanismSpec spec = JsonUtility.FromJson<MechanismSpec>(extractedJson);
                            if (spec != null && spec.joints != null && spec.joints.Length > 0)
                            {
                                Debug.Log("[NebiusSynthesizer] Synthesized mechanism via Nebius Token Factory.");
                                return spec;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[NebiusSynthesizer] JSON fallback: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[NebiusSynthesizer] HTTP {req.responseCode}: {req.error}. Using deterministic fallback.");
                }
            }

            return BuildDeterministicMechanism(caliperWidthMm);
        }

        public static MechanismSpec BuildDeterministicMechanism(double caliperWidthMm)
        {
            double clearanceM = 0.004;
            double spanM = System.Math.Max(0.03, (caliperWidthMm * 0.001) + clearanceM);
            double l0 = 0.06;
            double l1 = spanM * 1.5;
            double l2 = spanM * 1.2;

            return new MechanismSpec
            {
                robotName = $"kinetic_claw_{Mathf.RoundToInt((float)caliperWidthMm)}mm",
                targetCaliperWidthMm = caliperWidthMm,
                links = new LinkSpec[]
                {
                    new LinkSpec { name = "base_link", lengthM = 0.08, widthM = 0.08, heightM = l0, massKg = 0.25, centerX = 0, centerY = l0 * 0.5, centerZ = 0 },
                    new LinkSpec { name = "proximal_link", lengthM = l1, widthM = 0.025, heightM = 0.025, massKg = 0.15, centerX = l1 * 0.5, centerY = l0, centerZ = 0 },
                    new LinkSpec { name = "distal_jaw_link", lengthM = l2, widthM = 0.02, heightM = 0.02, massKg = 0.10, centerX = l1 + l2 * 0.5, centerY = l0, centerZ = 0 }
                },
                joints = new JointSpec[]
                {
                    new JointSpec
                    {
                        name = "base_yaw_joint", jointType = "revolute",
                        parentLink = "base_link", childLink = "proximal_link",
                        originX = 0.0, originY = l0, originZ = 0.0,
                        axisX = 0.0, axisY = 1.0, axisZ = 0.0,
                        minLimitRad = -3.14159, maxLimitRad = 3.14159
                    },
                    new JointSpec
                    {
                        name = "shoulder_pitch_joint", jointType = "revolute",
                        parentLink = "proximal_link", childLink = "distal_jaw_link",
                        originX = 0.0, originY = l0, originZ = 0.0,
                        axisX = 0.0, axisY = 0.0, axisZ = 1.0,
                        minLimitRad = -1.5708, maxLimitRad = 1.5708
                    },
                    new JointSpec
                    {
                        name = "elbow_pitch_joint", jointType = "revolute",
                        parentLink = "distal_jaw_link", childLink = "ee_tip",
                        originX = l1, originY = l0, originZ = 0.0,
                        axisX = 0.0, axisY = 0.0, axisZ = 1.0,
                        minLimitRad = -2.094, maxLimitRad = 2.094
                    }
                },
                endEffectorHomePosition = new double[] { l1 + l2, l0, 0.0 }
            };
        }

        private string BuildChatCompletionPayload(string userPrompt)
        {
            string escaped = userPrompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
            return $"{{\"model\":\"{_modelId}\",\"messages\":[{{\"role\":\"system\",\"content\":\"You are a deterministic robotics CAD compiler. Output strictly valid JSON.\"}},{{\"role\":\"user\",\"content\":\"{escaped}\"}}],\"temperature\":0.1,\"max_tokens\":900}}";
        }

        private static string ExtractInnerJsonObject(string rawResponse)
        {
            int contentIdx = rawResponse.IndexOf("\"content\"", StringComparison.Ordinal);
            if (contentIdx < 0) return null;
            int firstBrace = rawResponse.IndexOf('{', contentIdx);
            int lastBrace = rawResponse.LastIndexOf('}');
            if (firstBrace < 0 || lastBrace <= firstBrace) return null;
            return rawResponse.Substring(firstBrace, lastBrace - firstBrace + 1)
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n");
        }
    }
}