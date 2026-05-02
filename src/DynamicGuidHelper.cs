using System;
using System.Collections.Generic;
using System.Text;
using Kingmaker.Blueprints;

namespace CraftingSystem
{
    public static class DynamicGuidHelper
    {
        public const string Signature = "C2AF";

        /// <summary>
        /// Génère un GUID déterministe encodant l'ID de l'enchantement et ses paramètres.
        /// Format : [C2AF (4)] [EnchantId (3)] [Params (variable)] [00...00 (remplissage)]
        /// </summary>
        public static BlueprintGuid GenerateGuid(string enchantId, int[] parameters, bool isFeature = false, int mask = 0xFFF)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Signature);
            
            // On s'assure que l'ID fait 3 caractères (ex: "001")
            string id = enchantId ?? "000";
            if (id.Length > 3) id = id.Substring(0, 3);
            else while (id.Length < 3) id = "0" + id;
            
            sb.Append(id);

            // On prépare la liste finale (le flag isFeature est le PREMIER paramètre)
            List<int> finalParams = new List<int>();
            finalParams.Add(isFeature ? 1 : 0);
            if (parameters != null) finalParams.AddRange(parameters);

            // Nombre de paramètres (1 caractère hexa : 0-F)
            int count = finalParams.Count;
            sb.Append(count.ToString("X1"));

            // Encodage des paramètres (2 chars hexa par paramètre)
            foreach (int p in finalParams)
            {
                // On clamp à 0-255 pour tenir sur 2 caractères
                int clamped = Math.Max(0, Math.Min(255, p));
                sb.Append(clamped.ToString("X2"));
            }

            // Remplissage avec des zéros pour atteindre 29 caractères (32 - 3 pour le masque)
            while (sb.Length < 29)
            {
                sb.Append("0");
            }

            // Ajout du masque sur les 3 derniers caractères (12 bits : 0x000 à 0xFFF)
            int clampedMask = Math.Max(0, Math.Min(0xFFF, mask));
            sb.Append(clampedMask.ToString("X3"));

            string finalString = sb.ToString();
            if (finalString.Length > 32) finalString = finalString.Substring(0, 32);

            return BlueprintGuid.Parse(finalString);
        }

        /// <summary>
        /// Génère le GUID par défaut d'un modèle (tous paramètres à 0, masque complet).
        /// </summary>
        public static BlueprintGuid GenerateModelGuid(string enchantId, bool isFeature = false)
        {
            return GenerateGuid(enchantId, new int[0], isFeature, 0xFFF);
        }

        /// <summary>
        /// Décode un GUID pour retrouver l'ID de l'enchantement, ses paramètres et son masque.
        /// </summary>
        public static bool TryDecodeGuid(BlueprintGuid guid, out string enchantId, out List<int> parameters, out int mask)
        {
            enchantId = null;
            parameters = new List<int>();
            mask = 0xFFF; // Valeur par défaut si décodage impossible

            // On se base sur la version string pour plus de fiabilité vis-à-vis de l'endianness
            string s = guid.ToString().Replace("-", "").ToUpper();
            Main.ModEntry.Logger.Log($"[DEBUG_GUID] Deciphering: {s}"); 
            
            if (s.Length != 32 || !s.StartsWith(Signature))
            {
                Main.ModEntry.Logger.Warning($"[DEBUG_GUID] Signature mismatch or length error: {s}");
                return false;
            }

            enchantId = s.Substring(Signature.Length, 3);
            
            // Lecture du nombre de paramètres
            string countHex = s.Substring(Signature.Length + 3, 1);
            int count = Convert.ToInt32(countHex, 16);

            // On décode exactement 'count' paramètres
            int startIdx = Signature.Length + 3 + 1;
            for (int i = 0; i < count; i++)
            {
                int pos = startIdx + (i * 2);
                if (pos + 1 >= 29) break; // Ne pas empiéter sur le masque

                string hex = s.Substring(pos, 2);
                try {
                    parameters.Add(Convert.ToInt32(hex, 16));
                } catch {
                    break;
                }
            }

            // Lecture du masque (3 derniers caractères)
            try {
                string maskHex = s.Substring(29, 3);
                mask = Convert.ToInt32(maskHex, 16);
            } catch {
                mask = 0xFFF;
            }

            return true;
        }

        // Compatibilité pour l'ancien code ne demandant pas le masque
        public static bool TryDecodeGuid(BlueprintGuid guid, out string enchantId, out List<int> parameters)
        {
            return TryDecodeGuid(guid, out enchantId, out parameters, out _);
        }
    }
}
