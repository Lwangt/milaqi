using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Milaqi.Core
{
    public sealed class ArchetypeStyle
    {
        public float xpBias = 1f;
        public float rerollBias = 1f;
        public float saveBuffer = 4f;
        public float pricePenalty = 0f;   // 正值偏好低费兵，负值偏好高费兵
        public float popWeight = 1f;
        public float tierBias = 0f;   // 正值偏好高费精英，负值偏好低费铺场
        public float frontlineTarget = 0.38f;   // 理想的前排人口占比（阵容结构约束）
        public EffectDef[] factionBonus = Array.Empty<EffectDef>();   // 流派印记：固有加成
        public string[] skills = Array.Empty<string>();
    }

    public sealed class ArchetypeDef
    {
        public string id;
        public string name;
        public string icon;
        public string desc;
        public Dictionary<string, float> tagWeight = new Dictionary<string, float>();
        public Dictionary<string, float> unitWeight = new Dictionary<string, float>();
        public string[] relicPriority = Array.Empty<string>();
        public ArchetypeStyle style = new ArchetypeStyle();

        public float TagW(string tag)
        {
            float v;
            return tag != null && tagWeight != null && tagWeight.TryGetValue(tag, out v) ? v : 0f;
        }
        public float UnitW(string id)
        {
            float v;
            return id != null && unitWeight != null && unitWeight.TryGetValue(id, out v) ? v : 0f;
        }
        /// <summary>遗物在流派里的优先级：越靠前分越高（0~1）。</summary>
        public float RelicScore(string relicId)
        {
            if (relicPriority == null) return 0f;
            for (int i = 0; i < relicPriority.Length; i++)
                if (relicPriority[i] == relicId) return 1f - i * (0.9f / Math.Max(1, relicPriority.Length));
            return 0f;
        }
    }

    public sealed class DifficultyDef
    {
        public string id;
        public string name;
        public string desc;
        public float thinkInterval = 0.3f;
        public float relicPickQuality = 0.6f;   // 0=乱选 1=完全按流派最优
        public float counterPick = 0.3f;        // 针对敌方阵容选兵的权重
        public float rerollBias = 1f;
        public float xpBias = 1f;
        public float deployQuality = 0.6f;
        public bool useSkills = true;
        public float saveBuffer = 4f;
        public int reserveGold;
        public bool allowUpgrade = true;    // 是否会「卖弱兵换强兵」
        public int maxRerolls = 2;          // 已废弃（商店不再刷新），保留字段兼容
        public float buyRandomness = 0.1f;  // 买兵时的随机程度（越高越差）
        public float bonusGold = 0f;        // 每回合额外金币（难度档的经济差异）
        public float bonusXp = 0f;          // 每回合额外经验
    }

    public sealed class ArchetypeRoot
    {
        public ArchetypeDef[] archetypes;
        public DifficultyDef[] difficulties;
    }
}
