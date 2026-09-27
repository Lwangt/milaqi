using System;
using System.Collections.Generic;

namespace Milaqi.Core
{
    public enum Team { Left = 0, Right = 1 }

    public enum SimEventKind { Hit, Shot, Death, Spawn, Heal, Cast, Buff, Shield }

    public sealed class SimEvent
    {
        public SimEventKind kind;
        public float x, y, x2, y2;
        public Team team;
        public float value;
        public float radius;
        public string label;
    }

    public sealed class SimUnit
    {
        public int id;
        public UnitDef def;
        public Team team;
        public float x, y;
        public float hp, maxHp, shield;
        public float attackCd;
        public float slowUntil, slowPct;
        public float buffAtkUntil, buffAtkPct, buffSpdUntil, buffSpdPct;
        public float thornUntil, thornPct;
        public float dashCd;
        public float hitUntil;
        public float spawnedAt;
        public bool alive = true;
        public bool temporary;
        public ResolvedStats st;
        public float radius = 13f;

        public float Dist2(SimUnit o)
        {
            float dx = o.x - x, dy = o.y - y;
            return dx * dx + dy * dy;
        }
        public float Dist(SimUnit o) { return (float)Math.Sqrt(Dist2(o)); }
    }

    /// <summary>
    /// 纯 C# 战斗模拟：不依赖 Godot，可在 headless / 单元测试里直接跑。
    /// 坐标：左方基地 x=0，右方基地 x=fieldWidth。左方朝 +x 前进，右方朝 -x 前进。
    /// </summary>
    public sealed class BattleSim
    {
        public readonly GameDatabase db;
        public float fieldWidth = 1400f;
        public float fieldHeight = 420f;
        public float defConstant = 50f;
        public float minDamageRatio = 0.2f;
        public float tickRate = 30f;
        public float maxSeconds = 30f;
        public float time;

        readonly List<SimUnit> _units = new List<SimUnit>(512);
        readonly List<SimEvent> _events = new List<SimEvent>(2048);
        readonly List<SimUnit> _scratch = new List<SimUnit>(64);
        int _nextId = 1;
        int _tick;
        readonly List<int> _order = new List<int>(512);
        readonly int[] _teamSpawn = new int[2];
        float[] _pushX = new float[256];
        float[] _pushY = new float[256];
        uint _simRng = 2463534242u;

        /// <summary>用对局种子初始化战斗随机数。之前它是固定常量，导致所有对局完全相同。</summary>
        public void Seed(uint s) { _simRng = s == 0u ? 1u : s; }

        void ShuffleOrder()
        {
            _order.Clear();
            for (int i = 0; i < _units.Count; i++) if (_units[i].alive) _order.Add(i);
            for (int i = _order.Count - 1; i > 0; i--)
            {
                _simRng ^= _simRng << 13; _simRng ^= _simRng >> 17; _simRng ^= _simRng << 5;
                int j = (int)(_simRng % (uint)(i + 1));
                int t = _order[i]; _order[i] = _order[j]; _order[j] = t;
            }
        }

        public IReadOnlyList<SimUnit> Units { get { return _units; } }
        public List<SimEvent> Events { get { return _events; } }
        public int UnitCount { get { return _units.Count; } }

        public BattleSim(GameDatabase db)
        {
            this.db = db;
            var c = db.Balance.combat;
            fieldWidth = c.fieldWidth;
            fieldHeight = c.fieldHeight;
            defConstant = c.defConstant;
            minDamageRatio = c.minDamageRatio;
            tickRate = c.tickRate;
            maxSeconds = db.Balance.battleMaxSeconds;
        }

        public float BaseX(Team t) { return t == Team.Left ? 0f : fieldWidth; }
        public Team Enemy(Team t) { return t == Team.Left ? Team.Right : Team.Left; }

        public void ClearEvents() { _events.Clear(); }
        public void Reset() { _units.Clear(); _events.Clear(); time = 0f; _nextId = 1; _teamSpawn[0] = 0; _teamSpawn[1] = 0; }

        public SimUnit Spawn(UnitDef def, Team team, float x, float y, ResolvedStats st)
        {
            var u = new SimUnit
            {
                id = _nextId++,
                def = def,
                team = team,
                x = x,
                y = y,
                st = st,
                maxHp = st.hp,
                hp = st.hp,
                spawnedAt = time,
                radius = 10f + Math.Min(10f, def.pop * 1.6f) + (def.tier >= 4 ? 4f : 0f),
                // 初始攻击冷却按「队内第几个上场的」对称错开。
                // 之前用 id 哈希，左右两队的 id 区间不同 → 采样偏差导致近战阵容有 ~9% 的阵营优势。
                attackCd = 0.22f + (_teamSpawn[(int)team]++ % 5) * 0.07f,
            };
            _units.Add(u);
            _events.Add(new SimEvent { kind = SimEventKind.Spawn, x = x, y = y, team = team, value = def.tier });
            return u;
        }

        /// <summary>客户端用：按主机快照重建单位（不跑逻辑，只用于渲染）</summary>
        public SimUnit SpawnRaw(UnitDef def, Team team, float x, float y, float hp, float maxHp, float shield)
        {
            var st = StatResolver.Base(def);
            var u = new SimUnit
            {
                id = _nextId++,
                def = def,
                team = team,
                x = x,
                y = y,
                st = st,
                maxHp = maxHp > 0.01f ? maxHp : st.hp,
                hp = hp,
                shield = shield,
                radius = 10f + Math.Min(10f, def.pop * 1.6f) + (def.tier >= 4 ? 4f : 0f),
            };
            _units.Add(u);
            return u;
        }

        public void ClearUnits() { _units.Clear(); _teamSpawn[0] = 0; _teamSpawn[1] = 0; }

        public void RemoveTeam(Team t)
        {
            for (int i = _units.Count - 1; i >= 0; i--) if (_units[i].team == t) _units.RemoveAt(i);
        }

        static float Random01(int salt)
        {
            unchecked
            {
                uint h = (uint)(salt * 2654435761u);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h & 0xFFFF) / 65535f;
            }
        }

        public int AliveCount(Team t)
        {
            int n = 0;
            for (int i = 0; i < _units.Count; i++) if (_units[i].alive && _units[i].team == t) n++;
            return n;
        }

        public float KillValueSum(Team t)
        {
            float s = 0f;
            for (int i = 0; i < _units.Count; i++) if (_units[i].alive && _units[i].team == t) s += _units[i].st.killValue;
            return s;
        }

        public bool TeamWiped(Team t) { return AliveCount(t) == 0; }
        public bool BattleOver { get { return TeamWiped(Team.Left) || TeamWiped(Team.Right) || time >= maxSeconds; } }

        // ---------------------------------------------------------------- step
        public void Step(float dt)
        {
            if (dt <= 0f) return;
            time += dt;

            for (int i = 0; i < _units.Count; i++)
            {
                var u = _units[i];
                if (!u.alive) continue;
                if (u.attackCd > 0f) u.attackCd -= dt;
                if (u.dashCd > 0f) u.dashCd -= dt;
                if (u.slowPct > 0f && time >= u.slowUntil) u.slowPct = 0f;
            }

            // 单位处理顺序固定会让先处理的一方获得系统性先手优势（实测左右胜率偏差 18%）。
            // 每 tick 用 Fisher-Yates 打乱处理顺序，彻底消除顺序带来的阵营偏差。
            ShuffleOrder();
            for (int n = 0; n < _order.Count; n++)
            {
                int i = _order[n];
                var u = _units[i];
                if (!u.alive) continue;
                var target = AcquireTarget(u);
                if (target != null)
                {
                    float d = u.Dist(target);
                    float reach = u.st.range + target.radius * 0.5f;
                    if (d <= reach)
                    {
                        FaceAndAttack(u, target);
                    }
                    else
                    {
                        MoveToward(u, target.x, target.y, dt);
                    }
                }
                else
                {
                    MoveToward(u, BaseX(Enemy(u.team)), u.y, dt);
                }
            }

            Separate(dt);
            Compact();
        }

        SimUnit AcquireTarget(SimUnit u)
        {
            SimUnit nearest = null;
            float bestD2 = float.MaxValue;
            SimUnit back = null;
            float bestBack = float.MaxValue;
            bool wantsBack = u.st.targetBackline;
            for (int i = 0; i < _units.Count; i++)
            {
                var o = _units[i];
                if (!o.alive || o.team == u.team) continue;
                float d2 = u.Dist2(o);
                if (d2 < bestD2) { bestD2 = d2; nearest = o; }
                if (wantsBack && (o.def.HasTag("ranged") || o.def.HasTag("magic")))
                {
                    float fromOwnBase = Math.Abs(o.x - BaseX(o.team));
                    if (fromOwnBase < bestBack) { bestBack = fromOwnBase; back = o; }
                }
            }
            if (wantsBack && back != null) return back;
            return nearest;
        }

        void MoveToward(SimUnit u, float tx, float ty, float dt)
        {
            float dx = tx - u.x, dy = ty - u.y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) return;
            float spd = u.st.speed * (1f - Math.Min(0.9f, u.slowPct));
            if (u.st.dash && u.dashCd <= 0f && len > 160f)
            {
                spd *= 3f;
                u.dashCd = 6f;
                _events.Add(new SimEvent { kind = SimEventKind.Buff, x = u.x, y = u.y, team = u.team, label = "dash" });
            }
            float step = spd * dt;
            if (step > len) step = len;
            u.x += dx / len * step;
            u.y += dy / len * step;
            ClampToField(u);
        }

        void ClampToField(SimUnit u)
        {
            float pad = 8f;
            if (u.x < pad) u.x = pad;
            if (u.x > fieldWidth - pad) u.x = fieldWidth - pad;
            if (u.y < pad) u.y = pad;
            if (u.y > fieldHeight - pad) u.y = fieldHeight - pad;
        }

        /// <summary>
        /// 单位互相挤开。关键：先把所有推力累加进缓冲区，最后统一应用。
        /// 之前是边遍历边修改坐标，而左方单位永远排在列表前部，
        /// 导致累加结果与遍历顺序相关（纯战斗实测带来 20%+ 的阵营优势）。
        /// </summary>
        void Separate(float dt)
        {
            int n = _units.Count;
            if (_pushX.Length < n) { _pushX = new float[n * 2]; _pushY = new float[n * 2]; }
            for (int i = 0; i < n; i++) { _pushX[i] = 0f; _pushY[i] = 0f; }
            for (int i = 0; i < n; i++)
            {
                var a = _units[i];
                if (!a.alive) continue;
                for (int j = i + 1; j < n; j++)
                {
                    var b = _units[j];
                    if (!b.alive) continue;
                    float dx = b.x - a.x, dy = b.y - a.y;
                    float minD = a.radius + b.radius;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > minD * minD || d2 < 0.0001f) continue;
                    float d = (float)Math.Sqrt(d2);
                    float push = (minD - d) * 0.5f;
                    float nx = dx / d, ny = dy / d;
                    if (a.team == b.team) push *= 0.6f;
                    _pushX[i] -= nx * push; _pushY[i] -= ny * push;
                    _pushX[j] += nx * push; _pushY[j] += ny * push;
                }
            }
            for (int i = 0; i < n; i++)
            {
                var u = _units[i];
                if (!u.alive) continue;
                if (_pushX[i] != 0f || _pushY[i] != 0f)
                {
                    u.x += _pushX[i]; u.y += _pushY[i];
                    ClampToField(u);
                }
            }
        }

        void Compact()
        {
            for (int i = _units.Count - 1; i >= 0; i--) if (!_units[i].alive) _units.RemoveAt(i);
        }

        void FaceAndAttack(SimUnit a, SimUnit t)
        {
            if (a.attackCd > 0f) return;
            float dmg = ComputeDamage(a, t);
            float speedMul = (a.buffSpdUntil > time && a.buffSpdPct > 0f) ? (1f + a.buffSpdPct) : 1f;
            a.attackCd = 1f / Math.Max(0.05f, a.st.atkSpeed * speedMul);
            bool ranged = a.st.range > 60f;
            _events.Add(new SimEvent
            {
                kind = ranged ? SimEventKind.Shot : SimEventKind.Hit,
                x = a.x, y = a.y, x2 = t.x, y2 = t.y, team = a.team, value = dmg
            });

            ApplyDamage(t, dmg, a);

            if (a.st.splash > 0f)
            {
                float r2 = a.st.splash * a.st.splash;
                for (int k = 0; k < _order.Count; k++)
                {
                    var o = _units[_order[k]];
                    if (!o.alive || o == t || o.team == a.team) continue;
                    float dx = o.x - t.x, dy = o.y - t.y;
                    if (dx * dx + dy * dy <= r2) ApplyDamage(o, dmg * 0.6f, a);
                }
            }
            if (a.st.cleave > 0f)
            {
                float r2 = a.st.cleave * a.st.cleave;
                for (int k = 0; k < _order.Count; k++)
                {
                    var o = _units[_order[k]];
                    if (!o.alive || o == t || o.team == a.team) continue;
                    float dx = o.x - a.x, dy = o.y - a.y;
                    if (dx * dx + dy * dy <= r2) ApplyDamage(o, dmg * a.st.cleaveRatio, a);
                }
            }
            if (a.st.lifesteal > 0f)
            {
                a.hp = Math.Min(a.maxHp, a.hp + dmg * a.st.lifesteal);
                _events.Add(new SimEvent { kind = SimEventKind.Heal, x = a.x, y = a.y, team = a.team, value = dmg * a.st.lifesteal });
            }
            if (a.st.slow > 0f && t.alive)
            {
                t.slowPct = Math.Max(t.slowPct, a.st.slow);
                t.slowUntil = time + a.st.slowTime;
            }
        }

        public float EffectiveAtk(SimUnit a, float baseValue)
        {
            if (a.buffAtkUntil > time && a.buffAtkPct > 0f) return baseValue * (1f + a.buffAtkPct);
            return baseValue;
        }

        public float ComputeDamage(SimUnit a, SimUnit t)
        {
            float rawPhys = EffectiveAtk(a, a.st.atk);
            float rawMag = EffectiveAtk(a, a.st.matk);
            float phys = rawPhys * (1f - t.st.pdef / (t.st.pdef + defConstant));
            float mag = rawMag * (1f - t.st.mdef / (t.st.mdef + defConstant));
            // 伤害下限：任何攻击至少打出 20% 原始伤害，避免高防单位对低攻兵种完全免疫
            phys = Math.Max(phys, rawPhys * minDamageRatio);
            mag = Math.Max(mag, rawMag * minDamageRatio);
            float dmg = Math.Max(0f, phys) + Math.Max(0f, mag);
            if (a.st.antiCavalry > 0f && t.def.HasTag("cavalry")) dmg *= 1f + a.st.antiCavalry;
            if (a.st.bonusVsBig > 0f && t.def.pop >= 3) dmg *= 1f + a.st.bonusVsBig;
            return dmg;
        }

        public void ApplyDamage(SimUnit t, float dmg, SimUnit attacker)
        {
            if (t == null || !t.alive || dmg <= 0f) return;
            if (t.shield > 0f)
            {
                float absorbed = Math.Min(t.shield, dmg);
                t.shield -= absorbed;
                dmg -= absorbed;
            }
            t.hp -= dmg;
            t.hitUntil = time + 0.12f;
            if (attacker != null && t.thornUntil > time && t.thornPct > 0f && attacker.team != t.team)
            {
                attacker.hp -= dmg * t.thornPct;
                attacker.hitUntil = time + 0.12f;
                if (attacker.hp <= 0f && attacker.alive) KillUnit(attacker, t);
            }
            if (t.hp <= 0f && t.alive) KillUnit(t, attacker);
        }

        void KillUnit(SimUnit victim, SimUnit killer)
        {
            victim.alive = false;
            victim.hp = 0f;
            _events.Add(new SimEvent { kind = SimEventKind.Death, x = victim.x, y = victim.y, team = victim.team, value = victim.def.tier });

            if (killer != null && killer.alive && !string.IsNullOrEmpty(killer.st.summonOnKill))
            {
                var sd = db.Unit(killer.st.summonOnKill);
                if (sd != null)
                {
                    var st = StatResolver.Base(sd);
                    Spawn(sd, killer.team, victim.x, victim.y, st);
                }
            }
            if (!string.IsNullOrEmpty(victim.st.deathSummonUnit) && victim.st.deathSummonChance > 0f)
            {
                _simRng ^= _simRng << 13; _simRng ^= _simRng >> 17; _simRng ^= _simRng << 5;
                float r = (_simRng & 0xFFFF) / 65535f;
                if (r < victim.st.deathSummonChance)
                {
                    var sd = db.Unit(victim.st.deathSummonUnit);
                    if (sd != null) Spawn(sd, victim.team, victim.x, victim.y, StatResolver.Base(sd));
                }
            }
        }

        // ---------------------------------------------------------------- skills
        public void AreaDamage(float x, float y, float radius, Team caster, float amount, bool magic, string label)
        {
            _events.Add(new SimEvent { kind = SimEventKind.Cast, x = x, y = y, radius = radius, team = caster, label = label });
            float r2 = radius * radius;
            var targets = _scratch;
            targets.Clear();
            for (int i = 0; i < _units.Count; i++)
            {
                var o = _units[i];
                if (!o.alive || o.team == caster) continue;
                float dx = o.x - x, dy = o.y - y;
                if (dx * dx + dy * dy <= r2) targets.Add(o);
            }
            for (int i = 0; i < targets.Count; i++)
            {
                float dmg = amount;
                if (magic) dmg *= 1f - targets[i].st.mdef / (targets[i].st.mdef + defConstant * 2f);
                ApplyDamage(targets[i], Math.Max(0f, dmg), null);
            }
        }

        public void AreaHeal(float x, float y, float radius, Team caster, float amount, string label)
        {
            _events.Add(new SimEvent { kind = SimEventKind.Cast, x = x, y = y, radius = radius, team = caster, label = label });
            float r2 = radius * radius;
            for (int i = 0; i < _units.Count; i++)
            {
                var o = _units[i];
                if (!o.alive || o.team != caster) continue;
                float dx = o.x - x, dy = o.y - y;
                if (dx * dx + dy * dy <= r2)
                {
                    o.hp = Math.Min(o.maxHp, o.hp + amount);
                    _events.Add(new SimEvent { kind = SimEventKind.Heal, x = o.x, y = o.y, team = caster, value = amount });
                }
            }
        }

        public void AreaSlow(float x, float y, float radius, Team caster, float slow, float duration, float damage, string label)
        {
            _events.Add(new SimEvent { kind = SimEventKind.Cast, x = x, y = y, radius = radius, team = caster, label = label });
            float r2 = radius * radius;
            for (int i = 0; i < _units.Count; i++)
            {
                var o = _units[i];
                if (!o.alive || o.team == caster) continue;
                float dx = o.x - x, dy = o.y - y;
                if (dx * dx + dy * dy <= r2)
                {
                    o.slowPct = Math.Max(o.slowPct, slow);
                    o.slowUntil = time + duration;
                    if (damage > 0f) ApplyDamage(o, damage, null);
                }
            }
        }

        public void AreaBuff(float x, float y, float radius, Team caster, float atkPct, float atkSpeedPct, float duration, float shield, float thornPct, string label)
        {
            _events.Add(new SimEvent { kind = SimEventKind.Cast, x = x, y = y, radius = radius, team = caster, label = label });
            float r2 = radius * radius;
            for (int i = 0; i < _units.Count; i++)
            {
                var o = _units[i];
                if (!o.alive || o.team != caster) continue;
                float dx = o.x - x, dy = o.y - y;
                if (dx * dx + dy * dy <= r2)
                {
                    if (atkPct > 0f) { o.buffAtkPct = Math.Max(o.buffAtkPct, atkPct); o.buffAtkUntil = time + duration; }
                    if (atkSpeedPct > 0f) { o.buffSpdPct = Math.Max(o.buffSpdPct, atkSpeedPct); o.buffSpdUntil = time + duration; }
                    if (shield > 0f) o.shield += shield;
                    if (thornPct > 0f) { o.thornPct = Math.Max(o.thornPct, thornPct); o.thornUntil = time + duration; }
                }
            }
        }

        public void LineDamage(float x, float y, float dirX, float length, float halfWidth, Team caster, float amount, string label)
        {
            _events.Add(new SimEvent { kind = SimEventKind.Cast, x = x, y = y, x2 = x + dirX * length, y2 = y, radius = halfWidth, team = caster, label = label });
            for (int i = 0; i < _units.Count; i++)
            {
                var o = _units[i];
                if (!o.alive || o.team == caster) continue;
                float along = (o.x - x) * dirX;
                if (along < 0f || along > length) continue;
                if (Math.Abs(o.y - y) > halfWidth) continue;
                ApplyDamage(o, amount, null);
            }
        }

        public void DeathGaze(float x, float y, float radius, Team caster, float amount, float threshold, string label)
        {
            _events.Add(new SimEvent { kind = SimEventKind.Cast, x = x, y = y, radius = radius, team = caster, label = label });
            float r2 = radius * radius;
            for (int i = 0; i < _units.Count; i++)
            {
                var o = _units[i];
                if (!o.alive || o.team == caster) continue;
                if (o.hp / o.maxHp > threshold) continue;
                float dx = o.x - x, dy = o.y - y;
                if (dx * dx + dy * dy <= r2) ApplyDamage(o, amount, null);
            }
        }

        public void SummonAt(float x, float y, int count, Team caster, string unitId, string label)
        {
            var def = db.Unit(unitId);
            if (def == null) return;
            _events.Add(new SimEvent { kind = SimEventKind.Cast, x = x, y = y, radius = 70f, team = caster, label = label });
            for (int i = 0; i < count; i++)
            {
                float ox = x + (float)Math.Cos(i * 2.399) * 34f;
                float oy = y + (float)Math.Sin(i * 2.399) * 34f;
                Spawn(def, caster, Math.Max(12f, Math.Min(fieldWidth - 12f, ox)), Math.Max(12f, Math.Min(fieldHeight - 12f, oy)), StatResolver.Base(def));
            }
        }

        // 战斗阶段推进（返回是否结束）
        public bool StepBattle(float dt)
        {
            Step(dt);
            return BattleOver;
        }

        /// <summary>战后结算：双方存活单位杀戮值总和</summary>
        public void Settle(out float leftKv, out float rightKv)
        {
            leftKv = KillValueSum(Team.Left);
            rightKv = KillValueSum(Team.Right);
        }
    }
}
