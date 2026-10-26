using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;
using KineticCore.Schema;

namespace KineticCore.AI
{
    [Preserve]
    public class NebiusKinematicSynthesizer
    {
        private const string EndpointUrl = "https://api.tokenfactory.nebius.com/v1/chat/completions";
        public static string FastSuperModelId = "nvidia/nemotron-3-super-120b-a12b";
        public static string FallbackSuperModelId = "nvidia/Llama-3_3-Nemotron-Super-49B-v1";
        public static string DeepUltraModelId = "nvidia/nemotron-3-ultra-550b-a55b";
        public static string FallbackUltraModelId = "nvidia/Llama-3_1-Nemotron-Ultra-253B-v1";

        private readonly string _apiKey;
        public string KeyFilePath { get; private set; }
        public string KeySource { get; private set; } = "none";
        public bool HasValidKey => !string.IsNullOrEmpty(_apiKey);
        public string LastModelUsed { get; private set; } = "offline-parametric";
        public long LastLatencyMs { get; private set; } = 0;
        public string LastEngineeringAudit { get; private set; } = "Mobility M=3 DoF | Non-Singular | Ready";

        public NebiusKinematicSynthesizer(string overrideKey = null)
        {
            KeyFilePath = Path.Combine(Application.persistentDataPath, "nebius_key.txt");
            _apiKey = LoadApiKey(overrideKey, KeyFilePath, out string src);
            KeySource = src;
        }

        public static string LoadApiKey(string overrideKey, string filePath, out string sourceUsed)
        {
            if (!string.IsNullOrEmpty(overrideKey))
            {
                sourceUsed = "override";
                return overrideKey.Trim();
            }

            // 1. Check Application.persistentDataPath/nebius_key.txt (Quest 3 Android & PC)
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    string fileKey = File.ReadAllText(filePath).Trim();
                    if (!string.IsNullOrEmpty(fileKey))
                    {
                        sourceUsed = "persistentDataPath";
                        return fileKey;
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[NebiusSynthesizer] Could not read key file at {filePath}: {ex.Message}");
            }

            // 2. Check Unity Project Root /nebius_key.txt (instant Editor convenience on PC)
            try
            {
                DirectoryInfo parentDir = Directory.GetParent(Application.dataPath);
                if (parentDir != null)
                {
                    string rootKeyPath = Path.Combine(parentDir.FullName, "nebius_key.txt");
                    if (File.Exists(rootKeyPath))
                    {
                        string rootKey = File.ReadAllText(rootKeyPath).Trim();
                        if (!string.IsNullOrEmpty(rootKey))
                        {
                            sourceUsed = $"projectRoot ({rootKeyPath})";
                            return rootKey;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[NebiusSynthesizer] Could not check project root key file: {ex.Message}");
            }

            // 3. Check Resources/nebius_key.txt (bundled locally into Quest 3 APK when nebius_key.txt is present on PC, gitignored)
            try
            {
                TextAsset resKey = Resources.Load<TextAsset>("nebius_key");
                if (resKey != null && !string.IsNullOrEmpty(resKey.text))
                {
                    string trimmed = resKey.text.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        sourceUsed = "Resources/nebius_key";
                        return trimmed;
                    }
                }
            }
            catch { }

            // 4. Fallback to Environment Variable NEBIUS_API_KEY
            string envKey = Environment.GetEnvironmentVariable("NEBIUS_API_KEY");
            if (!string.IsNullOrEmpty(envKey))
            {
                sourceUsed = "env:NEBIUS_API_KEY";
                return envKey.Trim();
            }

            sourceUsed = "none";
            return "";
        }

        public static string LoadApiKey(string overrideKey, string filePath)
        {
            return LoadApiKey(overrideKey, filePath, out _);
        }

        public async Task<MechanismSpec> SynthesizeMechanismAsync(double caliperWidthMm, string userIntent)
        {
            Stopwatch sw = Stopwatch.StartNew();
            double clampedWidth = System.Math.Max(20.0, System.Math.Min(250.0, caliperWidthMm));

            if (string.IsNullOrEmpty(_apiKey))
            {
                sw.Stop();
                LastModelUsed = "offline-parametric (push nebius_key.txt)";
                LastLatencyMs = sw.ElapsedMilliseconds;
                return BuildDeterministicMechanism(clampedWidth);
            }

            string prompt =
                $"Design a 3-DoF spatial robotic mechanism sized for an object of width {clampedWidth:F1} mm. " +
                $"User intent: {userIntent}. Return ONLY valid JSON matching MechanismSpec with fields: " +
                $"robotName, targetCaliperWidthMm, links[], joints[], endEffectorHomePosition[3].";

            string[] modelsToTry = { FastSuperModelId, FallbackSuperModelId };
            foreach (string modelId in modelsToTry)
            {
                LastModelUsed = modelId;
                string requestBody = BuildChatCompletionPayload(modelId, prompt, 0.1, 800);

                using (UnityWebRequest req = new UnityWebRequest(EndpointUrl, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
                    req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    req.timeout = 10;

                    var op = req.SendWebRequest();
                    while (!op.isDone)
                        await Task.Yield();

                    sw.Stop();
                    LastLatencyMs = sw.ElapsedMilliseconds;

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        UnityEngine.Debug.Log($"[Nebius Super] 200 OK in {LastLatencyMs}ms via {modelId}");
                        string extractedJson = ExtractInnerJsonObject(req.downloadHandler.text);
                        if (!string.IsNullOrEmpty(extractedJson))
                        {
                            try
                            {
                                MechanismSpec spec = JsonUtility.FromJson<MechanismSpec>(extractedJson);
                                if (spec != null && spec.joints != null && spec.joints.Length > 0)
                                    return SanitizeSpec(spec, clampedWidth);
                            }
                            catch (Exception ex)
                            {
                                UnityEngine.Debug.LogWarning($"[Nebius Super] JSON fallback: {ex.Message}");
                            }
                        }
                        return BuildDeterministicMechanism(clampedWidth);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[Nebius Super] {modelId} HTTP {req.responseCode}: {req.error}");
                        LastModelUsed = $"{modelId} (HTTP {req.responseCode})";
                        sw.Start();
                    }
                }
            }

            return BuildDeterministicMechanism(clampedWidth);
        }

        public async Task<string> RunDeepKinematicAuditUltraAsync(MechanismSpec spec, double currentManipulability)
        {
            Stopwatch sw = Stopwatch.StartNew();
            double l1 = (spec.links != null && spec.links.Length > 1) ? spec.links[1].lengthM : 0.12;
            double l2 = (spec.links != null && spec.links.Length > 2) ? spec.links[2].lengthM : 0.09;
            double peakTorqueNm = 9.81 * 0.50 * (l1 + l2);

            string fallbackAudit =
                $"Kutzbach M=3 DoF | Reach={(l1 + l2) * 1000.0:F0}mm | Peak Torque={peakTorqueNm:F2}Nm | Kp=[45,38,22]";

            if (string.IsNullOrEmpty(_apiKey))
            {
                sw.Stop();
                LastModelUsed = "offline-parametric (push nebius_key.txt)";
                LastLatencyMs = sw.ElapsedMilliseconds;
                LastEngineeringAudit = fallbackAudit;
                return LastEngineeringAudit;
            }

            string prompt =
                $"Perform a 1-line mechanical & control audit for robot '{spec.robotName}' " +
                $"(width {spec.targetCaliperWidthMm:F1}mm, L1={l1:F3}m, L2={l2:F3}m, manipulability mu={currentManipulability:E2}). " +
                $"State Kutzbach mobility DoF, peak static torque in Nm, and ROS 2 PD gains in one line under 110 chars.";

            string[] modelsToTry = { DeepUltraModelId, FallbackUltraModelId };
            foreach (string modelId in modelsToTry)
            {
                LastModelUsed = modelId;
                string requestBody = BuildChatCompletionPayload(modelId, prompt, 0.15, 160);

                using (UnityWebRequest req = new UnityWebRequest(EndpointUrl, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
                    req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    req.timeout = 12;

                    var op = req.SendWebRequest();
                    while (!op.isDone)
                        await Task.Yield();

                    sw.Stop();
                    LastLatencyMs = sw.ElapsedMilliseconds;

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        UnityEngine.Debug.Log($"[Nebius Ultra] 200 OK in {LastLatencyMs}ms via {modelId}");
                        string content = ExtractContentText(req.downloadHandler.text);
                        if (!string.IsNullOrEmpty(content))
                        {
                            LastEngineeringAudit = content.Replace("\n", " ").Trim();
                            if (LastEngineeringAudit.Length > 120)
                                LastEngineeringAudit = LastEngineeringAudit.Substring(0, 120) + "...";
                            return LastEngineeringAudit;
                        }
                        LastEngineeringAudit = fallbackAudit;
                        return LastEngineeringAudit;
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[Nebius Ultra] {modelId} HTTP {req.responseCode}: {req.error}");
                        LastModelUsed = $"{modelId} (HTTP {req.responseCode})";
                        sw.Start();
                    }
                }
            }

            LastEngineeringAudit = fallbackAudit;
            return LastEngineeringAudit;
        }

        private static MechanismSpec SanitizeSpec(MechanismSpec spec, double fallbackWidthMm)
        {
            if (spec == null || spec.joints == null || spec.joints.Length < 3 || spec.links == null || spec.links.Length < 3)
                return BuildDeterministicMechanism(fallbackWidthMm);

            if (string.IsNullOrEmpty(spec.robotName))
                spec.robotName = $"kinetic_claw_{Mathf.RoundToInt((float)fallbackWidthMm)}mm";

            for (int i = 0; i < spec.links.Length; i++)
            {
                LinkSpec lk = spec.links[i];
                lk.lengthM = System.Math.Max(0.02, System.Math.Min(0.35, lk.lengthM > 0 ? lk.lengthM : 0.08));
                lk.widthM = System.Math.Max(0.015, System.Math.Min(0.12, lk.widthM > 0 ? lk.widthM : 0.025));
                lk.heightM = System.Math.Max(0.015, System.Math.Min(0.15, lk.heightM > 0 ? lk.heightM : 0.025));
                lk.massKg = System.Math.Max(0.05, System.Math.Min(2.0, lk.massKg > 0 ? lk.massKg : 0.15));
            }

            for (int i = 0; i < spec.joints.Length; i++)
            {
                JointSpec j = spec.joints[i];
                double mag = System.Math.Sqrt(j.axisX * j.axisX + j.axisY * j.axisY + j.axisZ * j.axisZ);
                if (mag < 1e-4)
                {
                    j.axisX = 0; j.axisY = (i == 0 ? 1.0 : 0.0); j.axisZ = (i == 0 ? 0.0 : 1.0);
                }
                if (j.maxLimitRad <= j.minLimitRad)
                {
                    j.minLimitRad = -2.0;
                    j.maxLimitRad = 2.0;
                }
            }

            if (spec.endEffectorHomePosition == null || spec.endEffectorHomePosition.Length != 3)
            {
                double l0 = spec.links[0].heightM;
                double l1 = spec.links[1].lengthM;
                double l2 = spec.links[2].lengthM;
                spec.endEffectorHomePosition = new double[] { l1 + l2, l0, 0.0 };
            }

            return spec;
        }

        public static MechanismSpec BuildDeterministicMechanism(double caliperWidthMm)
        {
            double clearanceM = 0.004;
            double spanM = System.Math.Max(0.04, System.Math.Min(0.25, (caliperWidthMm * 0.001) + clearanceM));
            double l0 = 0.06;
            double l1 = spanM * 1.4;
            double l2 = spanM * 1.15;

            return new MechanismSpec
            {
                robotName = $"kinetic_claw_{Mathf.RoundToInt((float)caliperWidthMm)}mm",
                targetCaliperWidthMm = caliperWidthMm,
                links = new LinkSpec[]
                {
                    new LinkSpec { name = "base_link", lengthM = 0.08, widthM = 0.08, heightM = l0, massKg = 0.25, centerX = 0, centerY = l0 * 0.5, centerZ = 0 },
                    new LinkSpec { name = "proximal_link", lengthM = l1, widthM = 0.026, heightM = 0.026, massKg = 0.15, centerX = l1 * 0.5, centerY = l0, centerZ = 0 },
                    new LinkSpec { name = "distal_jaw_link", lengthM = l2, widthM = 0.022, heightM = 0.022, massKg = 0.10, centerX = l1 + l2 * 0.5, centerY = l0, centerZ = 0 }
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

        private static string BuildChatCompletionPayload(string modelId, string userPrompt, double temp, int maxTokens)
        {
            string escaped = userPrompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
            return $"{{\"model\":\"{modelId}\",\"messages\":[{{\"role\":\"system\",\"content\":\"You are a deterministic neuro-symbolic robotics CAD compiler running on Nebius Token Factory.\"}},{{\"role\":\"user\",\"content\":\"{escaped}\"}}],\"temperature\":{temp.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"max_tokens\":{maxTokens}}}";
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

        private static string ExtractContentText(string rawResponse)
        {
            const string marker = "\"content\":\"";
            int idx = rawResponse.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;
            int start = idx + marker.Length;
            int end = rawResponse.IndexOf("\"", start, StringComparison.Ordinal);
            if (end <= start) return null;
            return rawResponse.Substring(start, end - start).Replace("\\n", " ").Replace("\\\"", "\"");
        }
    }
}
