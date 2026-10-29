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

        private const string SpecExampleJson =
            "{\"robotName\":\"claw_100mm\",\"targetCaliperWidthMm\":100," +
            "\"links\":[" +
            "{\"name\":\"base_link\",\"lengthM\":0.08,\"widthM\":0.08,\"heightM\":0.06,\"massKg\":0.25,\"centerX\":0,\"centerY\":0.03,\"centerZ\":0}," +
            "{\"name\":\"upper_arm_link\",\"lengthM\":0.15,\"widthM\":0.026,\"heightM\":0.026,\"massKg\":0.15,\"centerX\":0.075,\"centerY\":0.06,\"centerZ\":0}," +
            "{\"name\":\"forearm_link\",\"lengthM\":0.12,\"widthM\":0.022,\"heightM\":0.022,\"massKg\":0.10,\"centerX\":0.21,\"centerY\":0.06,\"centerZ\":0}]," +
            "\"joints\":[" +
            "{\"name\":\"base_yaw_joint\",\"jointType\":\"revolute\",\"parentLink\":\"base_link\",\"childLink\":\"turret_link\",\"originX\":0,\"originY\":0.06,\"originZ\":0,\"axisX\":0,\"axisY\":1,\"axisZ\":0,\"minLimitRad\":-3.14,\"maxLimitRad\":3.14}," +
            "{\"name\":\"shoulder_pitch_joint\",\"jointType\":\"revolute\",\"parentLink\":\"turret_link\",\"childLink\":\"upper_arm_link\",\"originX\":0,\"originY\":0.06,\"originZ\":0,\"axisX\":0,\"axisY\":0,\"axisZ\":1,\"minLimitRad\":-1.57,\"maxLimitRad\":1.57}," +
            "{\"name\":\"elbow_pitch_joint\",\"jointType\":\"revolute\",\"parentLink\":\"upper_arm_link\",\"childLink\":\"forearm_link\",\"originX\":0.15,\"originY\":0.06,\"originZ\":0,\"axisX\":0,\"axisY\":0,\"axisZ\":1,\"minLimitRad\":-2.09,\"maxLimitRad\":2.09}]," +
            "\"endEffectorHomePosition\":[0.27,0.06,0]}";

        /// <summary>True when the last returned MechanismSpec was produced by the language model (not the parametric fallback).</summary>
        public bool LastSpecFromAi { get; private set; } = false;

        /// <summary>Short human-readable description of where the last spec came from (shown on the HUD).</summary>
        public string LastSpecSource { get; private set; } = "parametric";

        public async Task<MechanismSpec> SynthesizeMechanismAsync(double caliperWidthMm, string userIntent)
        {
            Stopwatch sw = Stopwatch.StartNew();
            double clampedWidth = System.Math.Max(20.0, System.Math.Min(250.0, caliperWidthMm));
            LastSpecFromAi = false;

            if (string.IsNullOrEmpty(_apiKey))
            {
                sw.Stop();
                LastModelUsed = "offline-parametric (push nebius_key.txt)";
                LastLatencyMs = sw.ElapsedMilliseconds;
                LastSpecSource = "parametric (no API key)";
                return BuildDeterministicMechanism(clampedWidth);
            }

            string prompt =
                $"Design a 3-DoF serial robot arm sized to reach and grasp an object {clampedWidth:F1} mm wide. " +
                $"User intent: {userIntent}. " +
                "Rules: exactly 3 revolute joints and exactly 3 links, links[0] is the fixed base. " +
                "Units are metres. Y is up. All joint origins, link centres and endEffectorHomePosition are expressed in the base frame " +
                "with every joint at 0 rad, and the arm extends along +X at home. Keep every coordinate within 0.45 m of the origin. " +
                "Reply with ONLY one JSON object, no prose, using exactly these field names: " + SpecExampleJson;

            string lastFailure = "no response";
            string[] modelsToTry = { FastSuperModelId, FallbackSuperModelId };
            foreach (string modelId in modelsToTry)
            {
                LastModelUsed = modelId;
                string requestBody = BuildChatCompletionPayload(modelId, prompt, 0.1, 1500);

                using (UnityWebRequest req = new UnityWebRequest(EndpointUrl, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
                    req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    req.timeout = 25;

                    var op = req.SendWebRequest();
                    while (!op.isDone)
                        await Task.Yield();

                    sw.Stop();
                    LastLatencyMs = sw.ElapsedMilliseconds;

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        UnityEngine.Debug.Log($"[Nebius Super] 200 OK in {LastLatencyMs}ms via {modelId}");
                        string content = ExtractMessageContent(req.downloadHandler.text);
                        MechanismSpec parsed = TryParseMechanismSpec(content, out string parseFail);
                        if (parsed != null)
                        {
                            MechanismSpec valid = ValidateAndSanitizeSpec(parsed, clampedWidth, out string invalidReason);
                            if (valid != null)
                            {
                                LastSpecFromAi = true;
                                LastSpecSource = $"AI spec ({ShortModelName(modelId)})";
                                UnityEngine.Debug.Log($"[Nebius Super] Using AI-generated MechanismSpec '{valid.robotName}'");
                                return valid;
                            }
                            lastFailure = invalidReason;
                        }
                        else
                        {
                            lastFailure = parseFail;
                        }

                        UnityEngine.Debug.LogWarning($"[Nebius Super] {modelId} reply unusable ({lastFailure}); trying next model / fallback.");
                        sw.Start();
                    }
                    else
                    {
                        lastFailure = $"HTTP {req.responseCode}";
                        UnityEngine.Debug.LogWarning($"[Nebius Super] {modelId} HTTP {req.responseCode}: {req.error}");
                        LastModelUsed = $"{modelId} (HTTP {req.responseCode})";
                        sw.Start();
                    }
                }
            }

            sw.Stop();
            LastLatencyMs = sw.ElapsedMilliseconds;
            LastSpecSource = $"parametric fallback ({lastFailure})";
            return BuildDeterministicMechanism(clampedWidth);
        }

        private static string ShortModelName(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return "?";
            int slash = modelId.LastIndexOf('/');
            return slash >= 0 ? modelId.Substring(slash + 1) : modelId;
        }

        public async Task<string> RunDeepKinematicAuditUltraAsync(MechanismSpec spec, double currentManipulability)
        {
            Stopwatch sw = Stopwatch.StartNew();
            LastAuditFromAi = false;
            double l1 = (spec.links != null && spec.links.Length > 1) ? spec.links[1].lengthM : 0.12;
            double l2 = (spec.links != null && spec.links.Length > 2) ? spec.links[2].lengthM : 0.09;
            // Simple static estimate: 0.5 kg payload held horizontally at full reach (link masses ignored).
            double peakTorqueNm = 9.81 * 0.50 * (l1 + l2);

            string fallbackAudit =
                $"Offline estimate: M=3 DoF (Kutzbach, 3R) | Reach={(l1 + l2) * 1000.0:F0}mm | Static torque~{peakTorqueNm:F2}Nm @0.5kg | Kp=[45,38,22] default";

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
                $"(3 revolute joints, object width {spec.targetCaliperWidthMm:F1}mm, L1={l1:F3}m, L2={l2:F3}m, position manipulability mu={currentManipulability:E2}). " +
                $"State Kutzbach mobility DoF, peak static torque in Nm for a 0.5 kg payload at full reach, and suggested ROS 2 PD gains. " +
                $"Reply with one plain-text line under 110 characters and nothing else.";

            string[] modelsToTry = { DeepUltraModelId, FallbackUltraModelId };
            foreach (string modelId in modelsToTry)
            {
                LastModelUsed = modelId;
                string requestBody = BuildChatCompletionPayload(modelId, prompt, 0.15, 600);

                using (UnityWebRequest req = new UnityWebRequest(EndpointUrl, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
                    req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    req.timeout = 25;

                    var op = req.SendWebRequest();
                    while (!op.isDone)
                        await Task.Yield();

                    sw.Stop();
                    LastLatencyMs = sw.ElapsedMilliseconds;

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        UnityEngine.Debug.Log($"[Nebius Ultra] 200 OK in {LastLatencyMs}ms via {modelId}");
                        string content = ExtractMessageContent(req.downloadHandler.text);
                        if (!string.IsNullOrEmpty(content))
                        {
                            LastEngineeringAudit = content.Replace("\r", " ").Replace("\n", " ").Trim();
                            if (LastEngineeringAudit.Length > 120)
                                LastEngineeringAudit = LastEngineeringAudit.Substring(0, 120) + "...";
                            LastAuditFromAi = true;
                            return LastEngineeringAudit;
                        }
                        UnityEngine.Debug.LogWarning($"[Nebius Ultra] {modelId} returned no usable text; trying next model / fallback.");
                        sw.Start();
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[Nebius Ultra] {modelId} HTTP {req.responseCode}: {req.error}");
                        LastModelUsed = $"{modelId} (HTTP {req.responseCode})";
                        sw.Start();
                    }
                }
            }

            sw.Stop();
            LastLatencyMs = sw.ElapsedMilliseconds;
            LastEngineeringAudit = fallbackAudit;
            return LastEngineeringAudit;
        }

        /// <summary>True when the last engineering audit text came from the language model.</summary>
        public bool LastAuditFromAi { get; private set; } = false;

        /// <summary>
        /// Clamps an AI-generated spec into safe ranges and rejects geometry the runtime cannot use.
        /// Returns null (with a reason) instead of silently substituting the parametric design.
        /// </summary>
        private static MechanismSpec ValidateAndSanitizeSpec(MechanismSpec spec, double fallbackWidthMm, out string reason)
        {
            reason = null;
            if (spec == null) { reason = "null spec"; return null; }
            if (spec.joints == null || spec.joints.Length != 3) { reason = $"expected 3 joints, got {(spec.joints == null ? 0 : spec.joints.Length)}"; return null; }
            if (spec.links == null || spec.links.Length < 3) { reason = $"expected 3 links, got {(spec.links == null ? 0 : spec.links.Length)}"; return null; }

            const double maxCoord = 0.6;
            foreach (JointSpec j in spec.joints)
            {
                if (j == null) { reason = "null joint"; return null; }
                if (!Finite(j.originX, j.originY, j.originZ) || !Finite(j.axisX, j.axisY, j.axisZ)) { reason = "non-finite joint values"; return null; }
                if (System.Math.Abs(j.originX) > maxCoord || System.Math.Abs(j.originY) > maxCoord || System.Math.Abs(j.originZ) > maxCoord)
                { reason = "joint origin out of range (units?)"; return null; }
            }
            foreach (LinkSpec lk in spec.links)
            {
                if (lk == null) { reason = "null link"; return null; }
                if (!Finite(lk.centerX, lk.centerY, lk.centerZ) || !Finite(lk.lengthM, lk.widthM, lk.heightM)) { reason = "non-finite link values"; return null; }
                if (System.Math.Abs(lk.centerX) > maxCoord || System.Math.Abs(lk.centerY) > maxCoord || System.Math.Abs(lk.centerZ) > maxCoord)
                { reason = "link centre out of range (units?)"; return null; }
            }

            if (string.IsNullOrEmpty(spec.robotName))
                spec.robotName = $"kinetic_claw_{Mathf.RoundToInt((float)fallbackWidthMm)}mm";
            spec.targetCaliperWidthMm = fallbackWidthMm;

            for (int i = 0; i < spec.links.Length; i++)
            {
                LinkSpec lk = spec.links[i];
                if (string.IsNullOrEmpty(lk.name)) lk.name = i == 0 ? "base_link" : $"link_{i}";
                lk.lengthM = System.Math.Max(0.02, System.Math.Min(0.35, lk.lengthM > 0 ? lk.lengthM : 0.08));
                lk.widthM = System.Math.Max(0.015, System.Math.Min(0.12, lk.widthM > 0 ? lk.widthM : 0.025));
                lk.heightM = System.Math.Max(0.015, System.Math.Min(0.15, lk.heightM > 0 ? lk.heightM : 0.025));
                lk.massKg = System.Math.Max(0.05, System.Math.Min(2.0, lk.massKg > 0 ? lk.massKg : 0.15));
            }

            for (int i = 0; i < spec.joints.Length; i++)
            {
                JointSpec j = spec.joints[i];
                if (string.IsNullOrEmpty(j.name)) j.name = $"joint_{i}";
                j.jointType = "revolute";
                double mag = System.Math.Sqrt(j.axisX * j.axisX + j.axisY * j.axisY + j.axisZ * j.axisZ);
                if (mag < 1e-4)
                {
                    j.axisX = 0; j.axisY = (i == 0 ? 1.0 : 0.0); j.axisZ = (i == 0 ? 0.0 : 1.0);
                }
                else
                {
                    j.axisX /= mag; j.axisY /= mag; j.axisZ /= mag;
                }
                if (j.maxLimitRad <= j.minLimitRad)
                {
                    j.minLimitRad = -2.0;
                    j.maxLimitRad = 2.0;
                }
            }

            if (spec.endEffectorHomePosition == null || spec.endEffectorHomePosition.Length != 3 ||
                !Finite(spec.endEffectorHomePosition[0], spec.endEffectorHomePosition[1], spec.endEffectorHomePosition[2]))
            {
                double l0 = spec.links[0].heightM;
                double l1 = spec.links[1].lengthM;
                double l2 = spec.links[2].lengthM;
                spec.endEffectorHomePosition = new double[] { l1 + l2, l0, 0.0 };
            }

            // The tip must sit away from the last joint and inside a desk-scale workspace, or IK has nothing to do.
            JointSpec last = spec.joints[2];
            double dx = spec.endEffectorHomePosition[0] - last.originX;
            double dy = spec.endEffectorHomePosition[1] - last.originY;
            double dz = spec.endEffectorHomePosition[2] - last.originZ;
            double tipLever = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double tipRadius = System.Math.Sqrt(
                spec.endEffectorHomePosition[0] * spec.endEffectorHomePosition[0] +
                spec.endEffectorHomePosition[1] * spec.endEffectorHomePosition[1] +
                spec.endEffectorHomePosition[2] * spec.endEffectorHomePosition[2]);
            if (tipLever < 0.02) { reason = "end effector coincides with last joint"; return null; }
            if (tipRadius < 0.05 || tipRadius > 0.8) { reason = "end effector out of workspace range"; return null; }

            return spec;
        }

        private static bool Finite(double a, double b, double c)
        {
            return !(double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c) ||
                     double.IsInfinity(a) || double.IsInfinity(b) || double.IsInfinity(c));
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
                        parentLink = "base_link", childLink = "turret_link",
                        originX = 0.0, originY = l0, originZ = 0.0,
                        axisX = 0.0, axisY = 1.0, axisZ = 0.0,
                        minLimitRad = -3.14159, maxLimitRad = 3.14159
                    },
                    new JointSpec
                    {
                        name = "shoulder_pitch_joint", jointType = "revolute",
                        parentLink = "turret_link", childLink = "proximal_link",
                        originX = 0.0, originY = l0, originZ = 0.0,
                        axisX = 0.0, axisY = 0.0, axisZ = 1.0,
                        minLimitRad = -1.5708, maxLimitRad = 1.5708
                    },
                    new JointSpec
                    {
                        name = "elbow_pitch_joint", jointType = "revolute",
                        parentLink = "proximal_link", childLink = "distal_jaw_link",
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
            return $"{{\"model\":\"{modelId}\",\"messages\":[{{\"role\":\"system\",\"content\":\"You are a deterministic neuro-symbolic robotics CAD compiler running on Nebius Token Factory. Reply with the requested output only.\"}},{{\"role\":\"user\",\"content\":\"{escaped}\"}}],\"temperature\":{temp.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"max_tokens\":{maxTokens}}}";
        }

        // ------------------------------------------------------------------ Response parsing

        /// <summary>
        /// Returns the unescaped text of choices[0].message.content from an OpenAI-compatible chat completion,
        /// with any &lt;think&gt;...&lt;/think&gt; reasoning block and markdown code fences removed.
        /// Returns null when the content is missing or null.
        /// </summary>
        public static string ExtractMessageContent(string rawResponse)
        {
            if (string.IsNullOrEmpty(rawResponse)) return null;

            // Match the exact key "content" (this does not match "reasoning_content").
            int keyIdx = rawResponse.IndexOf("\"content\"", StringComparison.Ordinal);
            while (keyIdx >= 0)
            {
                int i = keyIdx + 9;
                while (i < rawResponse.Length && char.IsWhiteSpace(rawResponse[i])) i++;
                if (i < rawResponse.Length && rawResponse[i] == ':')
                {
                    i++;
                    while (i < rawResponse.Length && char.IsWhiteSpace(rawResponse[i])) i++;
                    if (i < rawResponse.Length && rawResponse[i] == '"')
                    {
                        string content = ReadJsonString(rawResponse, i, out _);
                        return content != null ? CleanModelText(content) : null;
                    }
                    return null; // "content": null
                }
                keyIdx = rawResponse.IndexOf("\"content\"", keyIdx + 9, StringComparison.Ordinal);
            }
            return null;
        }

        /// <summary>Reads a JSON string literal starting at the opening quote and unescapes it.</summary>
        private static string ReadJsonString(string s, int openQuote, out int endIndex)
        {
            StringBuilder sb = new StringBuilder();
            int i = openQuote + 1;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"') { endIndex = i; return sb.ToString(); }
                if (c == '\\' && i + 1 < s.Length)
                {
                    char e = s[i + 1];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 5 < s.Length &&
                                int.TryParse(s.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                    i += 2;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            endIndex = -1;
            return null; // unterminated string
        }

        /// <summary>Removes reasoning blocks and markdown fences from model output.</summary>
        private static string CleanModelText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string t = text;

            // Strip <think>...</think> blocks (and an unterminated leading <think> block).
            int open;
            while ((open = t.IndexOf("<think>", StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int close = t.IndexOf("</think>", open, StringComparison.OrdinalIgnoreCase);
                t = close >= 0 ? t.Remove(open, close + 8 - open) : t.Substring(0, open);
            }
            int strayClose = t.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (strayClose >= 0) t = t.Substring(strayClose + 8);

            return t.Replace("```json", "").Replace("```JSON", "").Replace("```", "").Trim();
        }

        /// <summary>
        /// Scans text for balanced top-level JSON objects (string-aware) and returns the first one that
        /// deserializes into a MechanismSpec with joints. Returns null if none is found.
        /// </summary>
        public static MechanismSpec TryParseMechanismSpec(string text, out string failReason)
        {
            failReason = "no JSON object in model reply";
            if (string.IsNullOrEmpty(text)) { failReason = "empty model reply"; return null; }

            int start = text.IndexOf('{');
            while (start >= 0)
            {
                int end = FindMatchingBrace(text, start);
                if (end < 0) { failReason = "truncated JSON (raise max_tokens)"; return null; }

                string candidate = text.Substring(start, end - start + 1);
                try
                {
                    MechanismSpec spec = JsonUtility.FromJson<MechanismSpec>(candidate);
                    if (spec != null && spec.joints != null && spec.joints.Length > 0)
                    {
                        failReason = null;
                        return spec;
                    }
                    failReason = "JSON had no joints[]";
                }
                catch (Exception ex)
                {
                    failReason = $"JSON parse error: {ex.Message}";
                }
                start = text.IndexOf('{', end + 1);
            }
            return null;
        }

        private static int FindMatchingBrace(string s, int openIdx)
        {
            int depth = 0;
            bool inString = false;
            for (int i = openIdx; i < s.Length; i++)
            {
                char c = s[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
