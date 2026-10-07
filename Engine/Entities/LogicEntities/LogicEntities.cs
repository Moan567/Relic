using Engine.Compilation;
using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Engine.Entities.LogicEntities.LogicCompare;

namespace Engine.Entities.LogicEntities
{
    [EntityDescriptor()]
    [RegisterEntityOutputs("OnMapSpawn")]
    public class LogicAuto : WorldEntity
    {
        public LogicAuto()
        {
            IsSimulated = false;
            Controller = new LogicAutoController();
        }
    }
    public class LogicAutoController : EntityController
    {
        public override void OnDespawn()
        {
        }

        public override void OnRender(GameTime gameTime)
        {
        }

        public override void OnSpawn()
        {
            entity.IgnoreCollision = true;
        }
        // I would ordinarily think to do this in OnSpawn, but it isnt certain that whatever entity may be receiving
        // out outputs will have been spawned at that point. This is probably something I'll fix for map loads, but for now
        // this works.
        public override void OnUpdate(GameTime gameTime)
        {
            entity.CallOutput("OnMapSpawn", entity);
            EntityManager.DespawnEntity(entity);
        }
    }

    [EntityDescriptor()]
    [RegisterEntityInputs("SetValue","ToggleValue","Test")]
    [RegisterEntityOutputs("OnTrue","OnFalse")]
    public class LogicBranch : WorldEntity
    {
        [ExposeValue("branch_result")]
        bool value;
        public LogicBranch()
        {
            AxisAlignedBox = true;
            IgnoreCollision = true;
            IsSimulated = false;

            RegisterInputLocally("ToggleValue", (s, e) => { value = !value; });
            RegisterInputLocally("SetValue", (s, e) => { value = s[0] == '1'; });
            RegisterInputLocally("Test", (s, e) => { if (value) { CallOutput("OnTrue", this); } else { CallOutput("OnFalse", this); } });
        }
    }

    [EntityDescriptor()]
    [ExposeEntityProperty("Initial Value", Rockwall.EntityPropertyType.Float)]
    [ExposeEntityProperty("Initial Comparison Value", Rockwall.EntityPropertyType.Float)]
    [RegisterEntityInputs("SetValue", "SetComparison", "Compare")]
    [RegisterEntityOutputs("OnLessThan", "OnEqualTo", "OnNotEqualTo", "OnGreaterThan")]
    public class LogicCompare : WorldEntity
    {
        public LogicCompare()
        {
            AxisAlignedBox = true;
            IsSimulated = false;

            var c = new LogicCompareController();
            Controller = c;

            RegisterInputLocally("SetValue",      (s, e) => { if(!float.TryParse(s, out var val)) return; c.value = val; });
            RegisterInputLocally("SetComparison", (s, e) => { if(!float.TryParse(s, out var val)) return; c.comparison = val; });
            RegisterInputLocally("Compare",       (s, e) => { c.Compare(); });
        }

        public class LogicCompareController : EntityController
        {
            public float value, comparison;

            public void Compare()
            {
                if (value > comparison)  entity.CallOutput("OnGreaterThan", entity, $"value:{value}", $"comparison:{comparison}");
                if (value < comparison)  entity.CallOutput("OnLessThan",    entity, $"value:{value}", $"comparison:{comparison}");
                if (value == comparison) entity.CallOutput("OnEqualTo",     entity, $"value:{value}", $"comparison:{comparison}");
                if (value != comparison) entity.CallOutput("OnNotEqualTo",  entity, $"value:{value}", $"comparison:{comparison}");
            }

            public override void OnDespawn()
            {
            }

            public override void OnRender(GameTime gameTime)
            {
            }

            public override void OnSpawn()
            {
                value = float.Parse(entity.properties[0].Value, CultureInfo.InvariantCulture);
                comparison = float.Parse(entity.properties[1].Value, CultureInfo.InvariantCulture);
                entity.IgnoreCollision = true;
            }

            public override void OnUpdate(GameTime gameTime)
            {
            }

            public override CustomSaveData CaptureCustomData()
            {
                return new CustomSaveData
                {
                    vals = new Dictionary<string, object>
                    {
                        {"value",value},
                        {"comparison",comparison}
                    }
                };
            }
            public override void RestoreCustomData(CustomSaveData? o)
            {
                if (!o.HasValue) return;

                value = (float)Convert.ChangeType(o.Value.vals["value"],typeof(float));
                comparison = (float)Convert.ChangeType(o.Value.vals["comparison"], typeof(float));
            }
        }
    }
    [EntityDescriptor()]
    [RegisterEntityInputs("Print")]
    public class LogicPrint : WorldEntity
    {
        public LogicPrint()
        {
            AxisAlignedBox = true;
            IgnoreCollision = true;
            IsSimulated = false;

            RegisterInputLocally("Print", (s, e) => { MainEngine.Instance.Console.WriteDirect(s); });
        }
    }
    [EntityDescriptor()]
    [RegisterEntityInputs("SetA","SetB","Add","Subtract","Divide","Multiply")]
    [RegisterEntityOutputs("OnValueComputed")]
    public class LogicArithmetic : WorldEntity
    {
        public LogicArithmetic()
        {
            AxisAlignedBox = true;
            IsSimulated = false;

            var c = new LogicArithmeticController();
            Controller = c;

            RegisterInputLocally("SetA", (s, e) => { c.valueA = float.Parse(s, CultureInfo.InvariantCulture); });
            RegisterInputLocally("SetB", (s, e) => { c.valueB = float.Parse(s, CultureInfo.InvariantCulture); });
            RegisterInputLocally("Add", (s, e) => { c.Add(); });
            RegisterInputLocally("Subtract", (s, e) => { c.Sub(); });
            RegisterInputLocally("Divide", (s, e) => { c.Div(); });
            RegisterInputLocally("Multiply", (s, e) => { c.Mul(); });
        }
        public class LogicArithmeticController : EntityController
        {
            public float valueA, valueB;
            [ExposeValue("previous_result")]
            private float valueC;

            public void Finalize(float c)
            {
                valueC = c;
                entity.CallOutput("OnValueComputed", entity, $"value_c:{c}", $"value_a:{valueA}", $"value_b:{valueB}");
            }

            public void Add()
            {
                Finalize(valueA + valueB);
            }
            public void Sub()
            {
                Finalize(valueA - valueB);
            }
            public void Div()
            {
                Finalize(valueA / valueB);
            }
            public void Mul()
            {
                Finalize(valueA * valueB);
            }

            public override void OnDespawn()
            {
            }

            public override void OnRender(GameTime gameTime)
            {
            }

            public override void OnSpawn()
            {
                entity.IgnoreCollision = true;
            }

            public override void OnUpdate(GameTime gameTime)
            {
            }

            public override CustomSaveData CaptureCustomData()
            {
                return new CustomSaveData
                {
                    vals = new Dictionary<string, object>
                    {
                        {"valueA",valueA},
                        {"valueB",valueB}
                    }
                };
            }
            public override void RestoreCustomData(CustomSaveData? o)
            {
                if (!o.HasValue) return;

                valueA = (float)Convert.ChangeType(o.Value.vals["valueA"], typeof(float));
                valueB = (float)Convert.ChangeType(o.Value.vals["valueB"], typeof(float));
            }
        }
    }
    [EntityDescriptor]
    [ExposeEntityProperty("Tick Interval", Rockwall.EntityPropertyType.Float, "How many seconds between each OnTick firing while this entity is active", defaultValue: "1")]
    [ExposeEntityProperty("Is Enabled", Rockwall.EntityPropertyType.Bool, "If false, no ticks will fire.", defaultValue: "1")]
    [RegisterEntityInputs("EnableTicking", "DisableTicking", "SetTickInterval")]
    [RegisterEntityOutputs("OnTick")]
    public class LogicTicker : WorldEntity
    {
        private bool isEnabled;
        private float tickTime;
        public LogicTicker()
        {
            IsSimulated = false;
            IgnoreCollision = true;
            Controller = new LogicTickerController(this);

            RegisterInputLocally("EnableTicking", (a,b) => isEnabled = true);
            RegisterInputLocally("DisableTicking", (a,b) => isEnabled = false);
            RegisterInputLocally("SetTickInterval", (a,b) => tickTime = float.TryParse(a, out var val) ? val : tickTime);
        }

        public class LogicTickerController(LogicTicker ticker) : EntityController
        {
            LogicTicker ticker = ticker;

            float timer = 0f;

            public override void OnDespawn()
            {
            }

            public override void OnRender(GameTime gameTime)
            {
            }

            public override void OnSpawn()
            {
                ticker.isEnabled = (bool)(entity.ReadProperty("Is Enabled", Rockwall.EntityPropertyType.Bool) ?? false);
                ticker.tickTime = (float)(entity.ReadProperty("Tick Interval", Rockwall.EntityPropertyType.Float) ?? 1f);
            }

            public override void OnUpdate(GameTime gameTime)
            {
                if (!ticker.isEnabled) return;

                timer -= MainEngine.PreviousFrameDelta;

                if (timer > 0f) return;
                timer = ticker.tickTime;

                entity.CallOutput("OnTick", entity);
            }
        }
    }
}
