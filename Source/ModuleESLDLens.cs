using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ESLDCore
{
    public class ModuleESLDLens : ModuleDeployableGenericPart
    {
        [KSPField(isPersistant = true)]
        public bool activated = false;

        [KSPField]
        public FloatCurve gain = new FloatCurve();

        [KSPField]
        public float minStandoff = 100;

        [KSPField]
        public float standoffBubble = 2500;

        [KSPField]
        public float constantEC = 0;

        [KSPField]
        public float gLimit = 0.5f;

        [KSPField]
        public string activeAnimationName = "";

        [KSPField]
        public bool toggleParticleControllers = true;
        List<Booots_Unity_Extensions.RotatingParticleController> particleControllers = new List<Booots_Unity_Extensions.RotatingParticleController>();

        [KSPField]
        public bool toggleParticleEmitters = false;
        List<KSPParticleEmitter> particleEmitters = new List<KSPParticleEmitter>();

        protected Animation activeAnim;

        // Info field only, no gameplay impacts:
        [KSPField]
        public float nominalGain = -1;
        [KSPField]
        public float nominalWidth = -1;
        [KSPField]
        public string alignment = "";   // Conjunction, Opposing, Bidirectional, Other

        public static Dictionary<int, ModuleESLDLens> SnapshotModules { get; } = new Dictionary<int, ModuleESLDLens>();

        public float LensingGain(Vector3d origin, Vector3d destination)
        {
            Vector3d lensPosition = vessel.GetWorldPos3D();
            float standoff = Math.Min((float)Vector3d.Distance(origin, lensPosition), (float)Vector3d.Distance(destination, lensPosition));
            if (standoff < minStandoff)
                return 1;
            Vector3 lensToOrigin = (origin - lensPosition).normalized;
            Vector3 lensToDest = (destination - lensPosition).normalized;
            Vector3 trip = (destination - origin).normalized;
            float angle = (float)Vector3.Angle(lensToDest, lensToOrigin);
            angle = Mathf.Clamp(angle, -180, 180);
            float gainF = gain.Evaluate(angle);
            //float dot = Math.Max(Vector3.Dot(lensToOrigin, trip), Vector3.Dot(lensToDest, trip));
            float dot = Vector3.Dot(lensToDest, trip);
            gainF *= dot;
            if (standoff < standoffBubble)
                gainF = Mathf.Lerp(1, gainF, Mathf.Pow(standoff / standoffBubble, 2));
            return gainF;
        }

        public static float GetLensingGain(Vector3d origin, Vector3d destination, List<ModuleESLDLens> lenses)
        {
            float value = 0;
            foreach (ModuleESLDLens lens in lenses)
            {
                value = Math.Max(lens.LensingGain(origin, destination), value);
            }
            return value;
        }

        public static float GetLensingGainRatio(Vector3d origin, Vector3d destination, List<ModuleESLDLens> lenses)
        {
            return GainToRatio(GetLensingGain(origin, destination, lenses));
        }

        public static float RatioToGain(float ratio)
        {
            if (ratio == 1)
                return 0;
            return Mathf.Log10(ratio) * 10;
        }
        public static float GainToRatio(float gain)
        {
            if (gain == 0)
                return 1;
            return Mathf.Pow(10, gain / 10);
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            SetActionsEvents(activated);
            SetActionsEventsBlock(!deployStateOnLoad.StartsWith("deployed", StringComparison.InvariantCultureIgnoreCase));

            if (toggleParticleControllers)
                particleControllers.AddRange(part.GetComponents<Booots_Unity_Extensions.RotatingParticleController>());
            if (toggleParticleEmitters)
                particleEmitters.AddRange(part.GetComponents<KSPParticleEmitter>());
            
            SetupAnimation();

            LensToggleEmit();
        }

        private void SetupAnimation()
        {
            if (part != null && !String.IsNullOrEmpty(activeAnimationName))
            {
                activeAnim = part.FindModelAnimators(activeAnimationName).FirstOrDefault();
                if (activeAnim == null)
                    Debug.LogWarningFormat("Animation not found! {0}", activeAnimationName);
                else
                {
                    activeAnim[activeAnimationName].wrapMode = WrapMode.Once;


                    Booots_Unity_Extensions.AnimationEventExtension.AddOrGetAnimationEventExtension(activeAnim)
                        .AddEvent(activeAnim[activeAnimationName].clip, 1.0f, LensToggleEmit);

                    if (activated)
                    {
                        activeAnim[activeAnimationName].normalizedTime = 1;
                        activeAnim.Play(activeAnimationName);
                    }
                    else
                    {
                        activeAnim[activeAnimationName].normalizedTime = 0;
                        activeAnim[activeAnimationName].speed = -10;
                        activeAnim.Play(activeAnimationName);
                    }
                }
            }
        }

        public void LensToggleEmit()
        {
            if (activeAnim[activeAnimationName].speed > 0)
            {
                for (int i = particleControllers.Count - 1; i >= 0; i--)
                    particleControllers[i].StartEmit();
                for (int i = particleEmitters.Count - 1; i >= 0; i--)
                    particleEmitters[i].emit = true;
            }
            else
            {
                for (int i = particleControllers.Count - 1; i >= 0; i--)
                    particleControllers[i].StopEmit();
                for (int i = particleEmitters.Count - 1; i >= 0; i--)
                    particleEmitters[i].emit = false;
            }
        }

        private void PlayAnimation(float speed)
        {
            if (activeAnim == null) return;
            activeAnim[activeAnimationName].normalizedSpeed = speed;
            activeAnim[activeAnimationName].speed = speed;
            activeAnim[activeAnimationName].normalizedTime = Mathf.Sign(speed) > 0 ? 0 : 1;
            activeAnim.Play(activeAnimationName);
        }

        protected override void CustomizeFSM()
        {
            on_deployed.OnEvent = UnblockActionsEvents;
            on_stow.OnEvent = BlockActionsEvents;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (activated && constantEC > 0)
            {
                double ECgotten = part.RequestResource(PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id, (double)(constantEC * TimeWarp.fixedDeltaTime));
                if (ECgotten < constantEC * TimeWarp.fixedDeltaTime * 0.9)
                {
                    ScreenMessages.PostScreenMessage("Lensing beacon shutting down - out of power.", 5, ScreenMessageStyle.UPPER_CENTER);
                    Deactivate();
                }
            }
            if (activated && FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude > gLimit)
            {
                ScreenMessages.PostScreenMessage("Warning: Too deep in gravity well.  Beacon has been shut down for safety.", 5, ScreenMessageStyle.UPPER_CENTER);
                Deactivate();
            }
        }

        [KSPEvent(name = "LensToggle", active = true, guiActive = true, guiName = "Toggle Lensing Beacon")]
        public void LensToggle()
        {
            if (activated)
                Deactivate();
            else
                Activate();
        }

        [KSPAction("Toggle Lensing Beacon")]
        public void ToggleLensAction(KSPActionParam param) => LensToggle();

        [KSPAction("Activate Lensing Beacon")]
        public void ActivateLensAction(KSPActionParam param) => Activate();

        [KSPAction("Deactivate Lensing Beacon")]
        public void DeactivateLensAction(KSPActionParam param) => Deactivate();

        public void Activate()
        {
            if (State != DeployState.Deployed)
                return;
            if (activated)
                return;
            if (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude > gLimit)
            {
                string thevar = (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") ? "the " : string.Empty;
                ScreenMessages.PostScreenMessage("Cannot activate!  Gravity from " + thevar + vessel.mainBody.name + " is too strong.", 5, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            if (vessel.altitude < (vessel.mainBody.Radius * .15f)) // Check for radius limit.
            {
                string thevar = (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") ? "the " : string.Empty;
                ScreenMessages.PostScreenMessage("Cannot activate!  Beacon is too close to " + thevar + vessel.mainBody.name + ".", 5, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (Lock())
            {
                activated = true;
                SetActionsEvents(true);
                PlayAnimation(1f);
            }
        }

        public void Deactivate()
        {
            if (!activated)
                return;

            if (Unlock())
            {
                activated = false;
                SetActionsEvents(false);
                PlayAnimation(-1f);
            }
        }

        private void SetActionsEvents(bool active)
        {
            Events["LensToggle"].guiName = (active ? "Deactivate" : "Activate") + " Lensing Beacon";
            Actions["ActivateLensAction"].active = !active;
            Actions["DeactivateLensAction"].active = active;
        }

        private void BlockActionsEvents() => SetActionsEventsBlock(true);
        private void UnblockActionsEvents() => SetActionsEventsBlock(false);

        private void SetActionsEventsBlock(bool block)
        {
            Events["LensToggle"].active = !block;
            Actions["ToggleLensAction"].active = !block;
            Actions["ActivateLensAction"].active = !block;
            Actions["DeactivateLensAction"].active = !block;
        }

        public static ModuleESLDLens InstantiateFromSnapshot(ProtoPartSnapshot partSnapshot, ProtoPartModuleSnapshot moduleSnapshot, int index = 0)
        {
            if (moduleSnapshot.moduleName != "ModuleESLDLens")
                return null;

            int instanceID = -1;
            if (moduleSnapshot.moduleValues.TryGetValue("__snapshottedModuleInstanceID", ref instanceID))
            {
                if (SnapshotModules.ContainsKey(instanceID))
                    return SnapshotModules[instanceID];
                else
                    moduleSnapshot.moduleValues.RemoveValue("__snapshottedModuleInstanceID");
            }

            ModuleESLDLens lens = UnityEngine.Component.Instantiate<ModuleESLDLens>(PartLoader.getPartInfoByName(partSnapshot.partName).partPrefab.FindModulesImplementing<ModuleESLDLens>()[index]);
            //moduleSnapshot.moduleRef = lens;  // Can't do this because it breaks game saving...
            instanceID = lens.GetInstanceID();
            moduleSnapshot.moduleValues.AddValue("__snapshottedModuleInstanceID", instanceID);
            SnapshotModules.Add(instanceID, lens);
            lens.part = new Part() { vessel = partSnapshot.pVesselRef.vesselRef };
            lens.snapshot = moduleSnapshot;
            SerializationHelper.LoadObjectFromConfig(lens, moduleSnapshot.moduleValues);
            lens.OnLoad(moduleSnapshot.moduleValues);
            return lens;
        }

        public override string GetInfo()
        {
            StringBuilder info = new StringBuilder();
            if (nominalGain < 1)
                nominalGain = gain.Curve.keys.Max(k => k.value);
            info.AppendFormat("Nominal gain: {0}", nominalGain).AppendLine();
            if (nominalWidth > 0)
                info.AppendFormat("Nominal beam width: {0}°", nominalWidth).AppendLine();
            if (!String.IsNullOrEmpty(alignment))
                info.AppendLine("Alignment type: " + alignment);
            info.AppendFormat("Gravity limit: {0} g", gLimit).AppendLine();
            if (constantEC > 0)
                info.AppendFormat("Electric Charge used: {0}/s", constantEC).AppendLine();
            if (minStandoff > 0)
                info.AppendFormat("Minimum standoff distance: {0} m", minStandoff).AppendLine();
            if (standoffBubble > minStandoff)
                info.AppendFormat("Standoff distance for full power: {0} m", standoffBubble).AppendLine();
            return info.ToString().TrimEnd(Environment.NewLine.ToCharArray());
        }

        public void OnDestroy()
        {
            if (SnapshotModules.ContainsKey(this.GetInstanceID()))
            {
                this.snapshot.moduleValues.RemoveValues("__snapshottedModuleInstanceID");
                SnapshotModules.Remove(this.GetInstanceID());
            }
        }
    }
}
