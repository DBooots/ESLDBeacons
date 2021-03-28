using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ESLDCore
{
    public class ESLDBeacon : PartModule, ISerializationCallbackReceiver
    {
        // Activation animation
        [KSPField]
        public string animationName = "";

        protected Animation anim;

        // Display beacon status in right click menu.
        [KSPField(guiName = "Beacon Status", guiActive = true)]
        public string beaconStatus;

        // Per-beacon G limit.
        [KSPField(isPersistant=true)]
        public float gLimitEff = 0.5f;

        [KSPField]
        public float gLimit = 0.5f;

        // Activated state.
        [KSPField(isPersistant = true)]
        public bool activated = false;

        // Beacon model (from part config).
        // Can be any string, but overrides cost parameters when equal to one of the original 4 models.
        [KSPField]
        public string beaconModel = string.Empty;

        // Cost equation:
        // M=tonnage, D=tripdist, f=massFctr, e=massExp, c=coef, p=distPow, b=baseMult, l=yardstick, d=2 if D>distpenalty
        // COST = M*b + M^(1+f*M^e+d) * (D/sqrt(l))^(1/2^p) * c

        // Cost parameter (from part config). Coefficient for total cost. (Does not affect base cost)
        [KSPField]
        public float coef = 0.01f;

        // Cost parameter (from part config). Coefficient for mass exponent.
        [KSPField]
        public float massFctr = 0.0002f;

        // Cost parameter (from part config). Second exponent of mass. (M^(1+f*M^e))
        [KSPField]
        public float massExp = 1f;

        // Cost parameter (from part config). Root order of distance cost. (tripdist^(1/(2^distPow)))
        [KSPField]
        public float distPow = 1;

        // Cost parameter (from part config). Coefficient for base cost from mass.
        [KSPField]
        public float baseMult = 0.25f;

        // Cost parameter (from part config). Distance beyond which cost becomes prohibitive. (Zero for infinite)
        [KSPField]
        public float distPenalty = 0;

        // Cost parameter (from part config). Cost added simply for using the beacon.
        [KSPField]
        public float baseCost = 0;

        // Cost multiplier. Applies to entire base jump cost.
        [KSPField]
        public float multiplier = 1;

        [KSPField]
        public float jumpPrecision = 10;

        [KSPField]
        public bool canJumpSelf = false;

        [KSPField]
        public bool builtInHCU = false;

        [KSPField]
        public bool jumpTargetable = true;

        public static float galaxyScalar = 1;

        [KSPField]
        public bool needsResourcesToBoot = false;
        
        // Display beacon operational floor in right click menu.
        [KSPField(guiName = "Lowest Altitude", guiActive = true, guiUnits = "km")]
        public double opFloor;

        // Charge to initialize beacon.
        [KSPField(guiName = "Charge to Activate", guiActive = false, guiUnits = " EC")]
        public double neededEC;

        // Charge to run beacon.
        [KSPField(guiName = "Electric Use", guiActive = false, guiUnits = " EC/s")]
        public double constantEC;

        public List<ESLDJumpResource> JumpResources { get => jumpResources; }
        protected List<ESLDJumpResource> jumpResources = new List<ESLDJumpResource>();
        [SerializeField]
        private List<string> serializedJumpResources = new List<string>();

        public bool UnsafeTransfer { get => !hasHCU; }
        public bool CarriesVelocity { get => !hasAMU; }
        public string Description { get => beaconModel; }
        public Vessel Vessel { get => vessel; }
        public float PathGLimit { get => gLimitEff; }

        [KSPField(isPersistant = true)]
        public bool hasAMU = false;
        [KSPField(isPersistant = true)]
        public bool hasHCU = false;
        [KSPField(isPersistant = true)]
        public bool hasGMU = false;
        [KSPField(isPersistant = true)]
        public bool hasSCU = false;
        [KSPField(isPersistant = true)]
        public double massBonus = 1;

        public const string RnodeName = "RESOURCE";
        Logger log = new Logger("ESLDCore:ESLDBeacons: ");

        private static int ECid = -1;
        public static Dictionary<int, ESLDBeacon> SnapshotModules { get; } = new Dictionary<int, ESLDBeacon>();

        public static ESLDBeacon InstantiateFromSnapshot(ProtoPartSnapshot partSnapshot, ProtoPartModuleSnapshot moduleSnapshot, int index = 0)
        {
            if (moduleSnapshot.moduleName != "ESLDBeacon")
                return null;

            int instanceID = -1;
            if (moduleSnapshot.moduleValues.TryGetValue("__snapshottedModuleInstanceID", ref instanceID))
            {
                if (SnapshotModules.ContainsKey(instanceID))
                    return SnapshotModules[instanceID];
                else
                    moduleSnapshot.moduleValues.RemoveValue("__snapshottedModuleInstanceID");
            }

            ESLDBeacon beacon = UnityEngine.Component.Instantiate<ESLDBeacon>(PartLoader.getPartInfoByName(partSnapshot.partName).partPrefab.FindModulesImplementing<ESLDBeacon>()[index]);
            //moduleSnapshot.moduleRef = beacon;    // Can't do this because it breaks game saving...
            instanceID = beacon.GetInstanceID();
            moduleSnapshot.moduleValues.AddValue("__snapshottedModuleInstanceID", instanceID);
            SnapshotModules.Add(instanceID, beacon);
            beacon.part = new Part() { vessel = partSnapshot.pVesselRef.vesselRef };
            beacon.snapshot = moduleSnapshot;
            SerializationHelper.LoadObjectFromConfig(beacon, moduleSnapshot.moduleValues);
            beacon.OnLoad(moduleSnapshot.moduleValues);
            return beacon;
        }

        public override void OnStart(StartState state)
        {
            if (ECid < 0)
                ECid = PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id;
            if (part != null)
            {
                if (animationName != "")
                {
                    anim = part.FindModelAnimators(animationName).FirstOrDefault();
                    if (anim == null)
                        log.Warning("Animation not found! " + animationName);
                    else
                    {
                        log.Debug("Animation found: " + animationName);
                        anim[animationName].wrapMode = WrapMode.Once;
                        if (activated)
                        {
                            anim[animationName].normalizedTime = 1;
                            anim.Play(animationName);
                        }
                        else
                        {
                            anim[animationName].normalizedTime = 0;
                            anim[animationName].normalizedSpeed = -10;
                            anim.Play(animationName);
                        }
                    }
                }
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                CheckOwnTechBoxes();
                opFloor = FindAcceptableAltitude(); // Update tooltip display.
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            
            foreach (ConfigNode rNode in node.GetNodes(RnodeName))
            {
                ESLDJumpResource existingResource = jumpResources.Find(r => String.Equals(r.name, rNode.GetValue("name")));
                if (existingResource == null)
                {
                    existingResource = new ESLDJumpResource();
                    existingResource.Load(rNode);
                    jumpResources.Add(existingResource);
                }
                else
                {
                    existingResource.Load(rNode);
                }
            }

            if (!node.HasValue("gLimitEff"))
                gLimitEff = gLimit;

            // Not sure if there should be a default resource...
            if (jumpResources.Count == 0 && (beaconModel == "LB10" || beaconModel == "LB15" || beaconModel == "LB100" || beaconModel == "IB1"))
            {
                log.Warning("Generating new ESLDJumpResource for legacy beacon savefile");
                jumpResources.Add(new ESLDJumpResource("Karborundum", ratio: 1));
            }
            SetFieldsEventsActions(activated);

            ConfigNode[] settingsNode = GameDatabase.Instance.GetConfigNodes("ESLDSettings");
            if (settingsNode.Length < 1)
            {
                ConfigNode save = new ConfigNode();
                ConfigNode data = new ConfigNode("ESLDSettings");
                data.AddValue("galaxyScalar", 1);
                save.AddNode(data);
                Debug.Log("Creating ESLD Settings file.");
                save.Save("GameData/ESLDBeacons/ESLDSettings.cfg");
            }
            else
            {
                if (float.TryParse(settingsNode[0].GetValue("galaxyScalar"), out float loadGalaxyScalar))
                    galaxyScalar = loadGalaxyScalar;
                else
                    Debug.LogError("Did not load ESLD settings.");
            }
        }

        public override void OnSave(ConfigNode node)
        {
            CheckOwnTechBoxes();
            base.OnSave(node);
            for (int i = 0; i < jumpResources.Count; i++)
            {
                ConfigNode resNode = node.AddNode(RnodeName);
                jumpResources[i].Save(resNode);
            }
        }

        public override void OnFixedUpdate()
        {
            for (int i = jumpResources.Count - 1; i >= 0; i--)
                jumpResources[i].GetFuelOnBoard(part);
            opFloor = FindAcceptableAltitude(); // Keep updating tooltip display. Also, needed EC.
            if (activated)
            {
                if (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude > gLimitEff)
                {
                    ScreenMessages.PostScreenMessage("Warning: Too deep in gravity well.  Beacon has been shut down for safety.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    BeaconShutdown();
                }
                if (vessel.altitude < (vessel.mainBody.Radius * 0.25f))
                {
                    string thevar = (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") ? "the " : string.Empty;
                    ScreenMessages.PostScreenMessage("Warning: Too close to " + thevar + vessel.mainBody.name + ".  Beacon has been shut down for safety.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    BeaconShutdown();
                }
                double ECgotten = part.RequestResource("ElectricCharge", constantEC * TimeWarp.fixedDeltaTime);
                if (!Double.IsNaN(constantEC) && (constantEC > 0) &&
                    ECgotten <= constantEC * TimeWarp.fixedDeltaTime * 0.9)
                {
                    ScreenMessages.PostScreenMessage("Warning: Electric Charge depleted.  Beacon has been shut down.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    log.Debug("Requested: " + constantEC + ", got: " + ECgotten / TimeWarp.fixedDeltaTime);
                    BeaconShutdown();
                }
                if (needsResourcesToBoot)
                {
                    foreach (ESLDJumpResource Jresource in jumpResources)
                    {
                        if (Jresource.neededToBoot && !RequireResource(Jresource.resID, double.Epsilon, false))
                        {
                            ScreenMessages.PostScreenMessage("Warning: " + Jresource.name + " depleted.  Beacon has been shut down.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                            BeaconShutdown();
                            break;
                        }
                    }
                }
                //part.AddThermalFlux(TimeWarp.fixedDeltaTime * constantEC * 10);  // Not feasible until the fluctuation with high warp is nailed down.
            }
        }

        public void Warp(Vessel target, Vessel destination, float precision, List<Part> unsafeParts = null)
        {
            float tripDist = Vector3.Distance(vessel.GetWorldPos3D(), destination.GetWorldPos3D());
            float tonnage = target.GetTotalMass();
            float cost = GetTripFinalCost(GetTripBaseCost(tripDist / ModuleESLDLens.GetLensingGainRatio(Vessel.GetWorldPos3D(), target.GetWorldPos3D(), HailerGUI.Lenses), tonnage), target, destination, tonnage, unsafeParts);
            HailerGUI.PathCheck pathCheck = new HailerGUI.PathCheck(vessel, destination, gLimitEff);
            if (pathCheck.clear)
            {
                if (jumpResources.All(res => RequireResource(res.resID, res.ratio * cost, false)))
                {
                    jumpResources.All(res => RequireResource(res.resID, res.ratio * cost, true));

                    HailerButton.Instance.Dazzle();

                    Vector3d transferVelOffset = GetJumpVelOffset(target, destination);
                    if (hasAMU) transferVelOffset = destination.orbit.vel;
                    Vector3d spread = ((UnityEngine.Random.onUnitSphere + UnityEngine.Random.insideUnitSphere) / 2) * precision;
                    // Making the spread less likely to throw you outside the SoI of the body.
                    if ((destination.orbit.pos + spread).magnitude > destination.mainBody.sphereOfInfluence)
                        spread = -spread;   // Negative random is equally random.

                    OrbitDriver vesOrb = target.orbitDriver;
                    Orbit orbit = vesOrb.orbit;
                    Orbit newOrbit = new Orbit(orbit.inclination, orbit.eccentricity, orbit.semiMajorAxis, orbit.LAN, orbit.argumentOfPeriapsis, orbit.meanAnomalyAtEpoch, orbit.epoch, orbit.referenceBody);
                    newOrbit.UpdateFromStateVectors(destination.orbit.pos + spread, transferVelOffset, destination.mainBody, Planetarium.GetUniversalTime());
                    target.Landed = false;
                    target.Splashed = false;
                    target.landedAt = string.Empty;

                    OrbitPhysicsManager.HoldVesselUnpack(60);

                    List<Vessel> allVessels = FlightGlobals.Vessels;
                    foreach (Vessel v in allVessels.AsEnumerable())
                    {
                        if (v.packed == false)
                            v.GoOnRails();
                    }

                    CelestialBody oldBody = target.orbitDriver.orbit.referenceBody;

                    orbit.inclination = newOrbit.inclination;
                    orbit.eccentricity = newOrbit.eccentricity;
                    orbit.semiMajorAxis = newOrbit.semiMajorAxis;
                    orbit.LAN = newOrbit.LAN;
                    orbit.argumentOfPeriapsis = newOrbit.argumentOfPeriapsis;
                    orbit.meanAnomalyAtEpoch = newOrbit.meanAnomalyAtEpoch;
                    orbit.epoch = newOrbit.epoch;
                    orbit.referenceBody = newOrbit.referenceBody;
                    orbit.Init();
                    orbit.UpdateFromUT(Planetarium.GetUniversalTime());
                    if (orbit.referenceBody != newOrbit.referenceBody)
                        vesOrb.OnReferenceBodyChange?.Invoke(newOrbit.referenceBody);

                    target.orbitDriver.pos = target.orbit.pos.xzy;
                    target.orbitDriver.vel = target.orbit.vel;

                    if (target.orbitDriver.orbit.referenceBody != oldBody)
                        GameEvents.onVesselSOIChanged.Fire(new GameEvents.HostedFromToAction<Vessel, CelestialBody>(target, oldBody, target.orbitDriver.orbit.referenceBody));

                    if (UnsafeTransfer)
                    {
                        if (unsafeParts == null)
                            unsafeParts = HailerGUI.GetHCUParts(target).Keys.ToList();
                        for (int i = unsafeParts.Count - 1; i >= 0; i--)
                        {
                            unsafeParts[i].explosionPotential = Mathf.Max(1, unsafeParts[i].explosionPotential);
                            unsafeParts[i].explode();
                        }
                        for (int i = vessel.parts.Count - 1; i >= 0; i--)
                        {
                            Part part = vessel.parts[i];
                            for (int j = part.protoModuleCrew.Count - 1; j >= 0; j--)
                            {
                                ProtoCrewMember crew = part.protoModuleCrew[j];
                                ScreenMessages.PostScreenMessage(crew.name + " was killed in transit!", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                                vessel.parts[i].RemoveCrewmember(crew);
                                crew.Die();
                            }
                        }
                    }
                }
                else
                {
                    ScreenMessages.PostScreenMessage("Jump failed!  Origin beacon did not have enough fuel to execute transfer.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
        }
        
        // Calculate base cost in units of Karborundum before penalties for a transfer.
        public float GetTripBaseCost(float tripdist, float tonnage)
            => GetTripBaseCost(tripdist, tonnage, distPenalty, distPow, baseMult, massFctr, massExp, coef, baseCost, beaconModel, multiplier);
        public static float GetTripBaseCost(float tripdist, float tonnage, float distPenalty, float distPow, float baseMult, float massFctr, float massExp, float coef, float baseCost, string beaconModel = "", float multiplier = 1)
        {
            tripdist *= galaxyScalar;
            float yardstick = Mathf.Pow(13599840256, 1 / (distPow + 1)); //Math.Sqrt(Math.Sqrt(13599840256));
            float distPenaltyCost = 0;
            switch (beaconModel)
            {
                case "IB1":
                    massFctr = 1 / 6000;
                    coef = Convert.ToSingle(0.9 * Math.Pow((1 + tripdist * 2E11), 1 / 4));
                    distPow = 1;
                    massExp = 1f;
                    baseMult = 0f;
                    distPenalty = 0;
                    return ((((Mathf.Pow(tonnage, 1 + (tonnage / 6000)) * 0.9f) / 10) * ((Mathf.Sqrt(Mathf.Sqrt(tripdist + 2E11f))) / yardstick) / tonnage * 10000) * tonnage / 2000) * multiplier;
                default:
                    if ((distPenalty > 0) && (tripdist > distPenalty))
                        distPenaltyCost = 2;
                    yardstick = Mathf.Pow(13599840256, 1 / Mathf.Pow(2, distPow + 1)); //Math.Sqrt(Math.Sqrt(13599840256));
                    return (tonnage * baseMult + Mathf.Pow(tonnage, 1 + massFctr * Mathf.Pow(tonnage, massExp) + distPenaltyCost) * Mathf.Pow(tripdist, 1 / Mathf.Pow(2, distPow)) * coef / yardstick + baseCost) * multiplier;
            }
        }

        public float GetTripFinalCost(float baseCost, Vessel target, Vessel destination, float tonnage, List<Part> HCUParts = null)
            => GetTripFinalCost(baseCost, vessel, target, destination, tonnage, hasSCU, hasAMU, hasHCU, HCUParts, multiplier);
        public static float GetTripFinalCost(float baseCost, Vessel beacon, Vessel target, Vessel destination, float tonnage, bool hasSCU, bool hasAMU, bool hasHCU, List<Part> HCUParts = null, float multiplier = 1)
        {
            float cost = baseCost;
            if (hasSCU)
                cost *= 0.9f;
            cost *= GetCrewBonuses(beacon, "Scientist", 0.5f, 5);
            float distance = Vector3.Distance(beacon.GetWorldPos3D(), target.GetWorldPos3D());
            float relVel = Vector3.Magnitude(beacon.obt_velocity - target.obt_velocity);
            float driftpenalty = GetDriftPenalty(distance, relVel, GetCrewBonuses(target, "Pilot", 0.5f, 5));
            cost += cost * (driftpenalty * 0.01f);
            if (hasAMU)
                cost += GetAMUCost(target, destination, tonnage, multiplier);
            if (hasHCU)
                cost += GetHCUCost(target, HCUParts, multiplier);
            return cost;
        }

        public static float GetDriftPenalty(float distance, float relVelocity, float crewBonusMultiplier)
        {
            float driftpenalty = Mathf.Pow(distance / 200, 2) + Mathf.Pow(relVelocity, 1.5f) * crewBonusMultiplier;
            if (driftpenalty < 1)
                driftpenalty = 0;
            return driftpenalty;
        }

        public List<string> GetCostModifiers(Vessel target, Vessel destination, float tonnage, List<Part> HCUParts = null)
            => GetCostModifiers(vessel, target, destination, tonnage, hasSCU, hasAMU, hasHCU, jumpResources, HCUParts, multiplier);
        public static List<string> GetCostModifiers(Vessel beacon, Vessel target, Vessel destination, float tonnage, bool hasSCU, bool hasAMU, bool hasHCU, List<ESLDJumpResource> jumpResources, List<Part> HCUParts = null, float multiplier = 1)
        {
            List<string> modifiers = new List<string>();
            if (hasSCU)
                modifiers.Add("Superconducting Coil Array reduces cost by 10%.");
            float sciBonus = GetCrewBonuses(beacon, "Scientist", 0.5f, 5);
            if (sciBonus < 1)
                modifiers.Add(String.Format("Scientists on beacon vessel reduce cost by {0:P0}%.", (1 - sciBonus)));
            float distance = Vector3.Distance(beacon.GetWorldPos3D(), target.GetWorldPos3D());
            float relVel = Vector3.Magnitude(beacon.obt_velocity - target.obt_velocity);
            float driftpenalty = GetDriftPenalty(distance, relVel, GetCrewBonuses(target, "Pilot", 0.5f, 5));
            if (driftpenalty > 0)
                modifiers.Add(String.Format("Relative speed and distance to beacon adds {0:F2}%.", driftpenalty));
            if (hasAMU)
            {
                string amuStr = "AMU Compensation adds ";
                float AMUCost = GetAMUCost(target, destination, tonnage, multiplier);
                for (int i = 0; i < jumpResources.Count; i++)
                {
                    amuStr += String.Format("{0:F2} {1}{2}", AMUCost * jumpResources[i].ratio, jumpResources[i].name, i + 1 < jumpResources.Count ? ", " : "");
                }
                modifiers.Add(amuStr + ".");
            }
            if (hasHCU)
            {
                string hcuStr = "HCU Shielding adds ";
                float HCUCost = GetHCUCost(target, HCUParts, multiplier);
                for (int i = 0; i < jumpResources.Count; i++)
                {
                    hcuStr += String.Format("{0:F2} {1}{2}", HCUCost * jumpResources[i].ratio, jumpResources[i].name, i + 1 < jumpResources.Count ? ", " : "");
                }
                modifiers.Add(hcuStr + ".");
            }
            return modifiers;
        }

        // Calculate AMU cost in units of Karborundum given two vessel endpoints and the tonnage of the transferring vessel.
        public static float GetAMUCost(Vessel near, Vessel far, float tton, float multiplier)
        {
            Vector3 velDiff = GetJumpVelOffset(near, far) - far.orbit.vel;
            float comp = velDiff.magnitude;
            return ((comp * tton) / Mathf.Pow(Mathf.Log10(comp * tton), 2)) / 2 / 100 * multiplier;
        }

        public static float GetHCUCost(Vessel vessel, IEnumerable<Part> HCUParts = null, float multiplier =  1)
        {
            float HCUCost = 0;
            if (HCUParts == null)
                HCUParts = HailerGUI.GetHCUParts(vessel).Keys.ToList();
            foreach (Part vpart in HCUParts)
            {
                foreach (PartResource vres in vpart.Resources)
                {
                    if (HailerGUI.highEnergyResources.ContainsKey(vres.resourceName) && vres.amount > 0)
                    {
                        HCUCost += (vres.info.density * (float)vres.amount * 0.1f) / 0.0058f * HailerGUI.highEnergyResources[vres.resourceName];
                    }
                }
            }
            HCUCost += vessel.GetCrewCount() * 0.9f / 1.13f;
            return HCUCost * multiplier;
        }

        // Calculate how far away from a beacon the ship will arrive.
        public float GetTripSpread(float tripdist) => GetTripSpread(tripdist, jumpPrecision);
        public static float GetTripSpread(float tripdist, float jumpPrecision)
            => Mathf.Round(Mathf.Log(tripdist) / Mathf.Log(jumpPrecision) * 10) * 100;

        // Calculate Jump Velocity Offset
        public static Vector3 GetJumpVelOffset(Vessel nearObject, Vessel farObject)
        {
            Vector3 farRealVelocity = farObject.orbit.vel;
            CelestialBody farRefbody = farObject.mainBody;
            while (farRefbody.flightGlobalsIndex != 0) // Kerbol
            {
                farRealVelocity += farRefbody.orbit.vel;
                farRefbody = farRefbody.referenceBody;
            }
            Vector3 nearRealVelocity = nearObject.orbit.vel;
            CelestialBody nearRefbody = nearObject.mainBody;
            if (nearObject.mainBody.flightGlobalsIndex == farObject.mainBody.flightGlobalsIndex)
            {
                farRealVelocity -= farObject.orbit.vel;
                //log.Debug("In-system transfer, disregarding far beacon velocity.");
            }
            while (nearRefbody.flightGlobalsIndex != 0) // Kerbol
            {
                nearRealVelocity += nearRefbody.orbit.vel;
                nearRefbody = nearRefbody.referenceBody;
            }
            return nearRealVelocity - farRealVelocity;
        }

        public float GetCrewBonuses(string neededTrait, float maxBenefit, int countCap)
            => GetCrewBonuses(vessel, neededTrait, maxBenefit, countCap);
        public static float GetCrewBonuses(Vessel vessel, string neededTrait, float maxBenefit, int countCap)
        {
            float bonus = 0;
            int ccount = 0;
            foreach (ProtoCrewMember crew in vessel.GetVesselCrew())
            {
                if (crew.experienceTrait.Title == neededTrait) 
                {
                    bonus += crew.experienceLevel;
                    ccount += 1;
                }
            }
            if (ccount == 0) return 1;
            float bonusAvg = bonus / ccount;
            if (ccount > countCap) { ccount = countCap; }
            float endBenefit = 1 - (maxBenefit * ((ccount * bonusAvg) / 25));
            return endBenefit;
        }

        // Given a target body, get minimum ASL where beacon can function in km.
        public double FindAcceptableAltitude()
            => FindAcceptableAltitude(vessel.mainBody);

        public double FindAcceptableAltitude(CelestialBody targetbody)
        {
            gLimitEff = gLimit;
            double neededMult = 10;
            double constantDiv = 50;
            double baseLimit = gLimitEff;
            if (hasGMU)
            {
                gLimitEff *= 1.25f;
                neededMult = 15;
                constantDiv = 5;
                double massOffset = (Math.Pow(Math.Abs(Math.Log(vessel.GetTotalMass() / 10, 2.25)), 4) * baseLimit) / 1500;
                massBonus = 1 - massOffset;
                if (massBonus < 0)
                    massBonus = 0;
            }
            double limbo = (Math.Sqrt((6.673E-11 * targetbody.Mass) / gLimitEff) - targetbody.Radius) * massBonus;
            gLimitEff = Convert.ToSingle((6.673E-11 * targetbody.Mass) / Math.Pow(limbo + targetbody.Radius, 2));
            if (limbo < targetbody.Radius * 0.25) limbo = targetbody.Radius * 0.25;
            double fuelECMult = 0;
            foreach (ESLDJumpResource Jresource in jumpResources)
                fuelECMult += Math.Max(Jresource.fuelOnBoard * Jresource.ECMult, Jresource.minEC);
            neededEC = Math.Round((fuelECMult * neededMult * (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude / baseLimit)) * GetCrewBonuses("Engineer", 0.5f, 5));
            constantEC = Math.Round(fuelECMult / constantDiv * (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude / baseLimit) * 100 * GetCrewBonuses("Engineer", 0.5f, 5)) / 100;
            return Math.Round(limbo / 1000);
        }

        public void CheckOwnTechBoxes()
        {
            if (vessel == null)
                return;
            hasAMU = false;
            hasHCU = false;
            hasGMU = false;
            hasSCU = false;
            if (builtInHCU)
                hasHCU = true;
            foreach (ESLDTechbox techbox in vessel.FindPartModulesImplementing<ESLDTechbox>())
            {
                if (techbox.activated || techbox.alwaysActive)
                {
                    switch (techbox.techBoxModel.ToUpper())
                    {
                        case "AMU":
                            hasAMU = true;
                            break;
                        case "HCU":
                            hasHCU = true;
                            break;
                        case "GMU":
                            hasGMU = true;
                            break;
                        case "SCU":
                            hasSCU = true;
                            break;
                    }
                }
            }
        }

        // Simple bool for resource checking and usage.  Returns true and optionally uses resource if resAmount of res is available.
        public bool RequireResource(string res, double resAmount, bool consumeResource = false)
            => RequireResource(PartResourceLibrary.Instance.GetDefinition(res).id, resAmount, consumeResource);

        public bool RequireResource(int resID, double resAmount, bool consumeResource = false)
        {
            if (Double.IsNaN(resAmount))
            {
                log.Error("NaN requested.");
                return true;
            }

            if (!vessel.loaded)
            {
                log.Warning("Tried to get resources of unloaded craft.");
                return false; // Unloaded resource checking is unreliable.
            }

            part.GetConnectedResourceTotals(resID, out double amount, out _);

            if (amount < resAmount)
                return false;

            if (consumeResource)
                part.RequestResource(resID, resAmount);
            return true;
        }

        public bool CheckInitialize()
        {
            if (needsResourcesToBoot)
            {
                foreach (ESLDJumpResource Jresource in jumpResources)
                {
                    if (Jresource.neededToBoot && !RequireResource(Jresource.resID, double.Epsilon, false))
                    {
                        ScreenMessages.PostScreenMessage("Cannot activate!  Insufficient " + Jresource.name + " to initiate reaction.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                        return false;
                    }
                }
            }
            if (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude > gLimitEff) // Check our G forces.
            {
                log.Warning("Too deep in gravity well to activate!");
                string thevar = (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") ? "the " : string.Empty;
                ScreenMessages.PostScreenMessage("Cannot activate!  Gravity from " + thevar + vessel.mainBody.name + " is too strong.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            if (vessel.altitude < (vessel.mainBody.Radius * .25f)) // Check for radius limit.
            {
                string thevar = (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") ? "the " : string.Empty;
                ScreenMessages.PostScreenMessage("Cannot activate!  Beacon is too close to " + thevar + vessel.mainBody.name + ".", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            if (!RequireResource("ElectricCharge", neededEC, true))
            {
                ScreenMessages.PostScreenMessage("Cannot activate!  Insufficient electric power to initiate reaction.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                log.Debug("Couldn't activate, " + neededEC + " EC needed.");
                return false;
            }
            return true;
        }

        // Startup sequence for beacon.
        [KSPEvent(name="BeaconInitialize", active = true, guiActive = true, guiName = "Initialize Beacon")]
        public void BeaconInitialize()
        {
            if (!activated)
            {
                CheckOwnTechBoxes();
                log.Info("Crew bonus: Engineers on board reduce electrical usage by: " + (1 - GetCrewBonuses("Engineer", 0.5f, 5)) * 100 + "%");
                log.Info("Crew bonus: Scientists on board reduce jump costs by: " + (1 - GetCrewBonuses("Scientist", 0.5f, 5)) * 100 + "%");
                log.Info("Crew bonus: Pilots on board reduce drift by: " + (1 - GetCrewBonuses("Pilot", 0.5f, 5)) * 100 + "%");
                if (!CheckInitialize())
                    return;
                //          part.AddThermalFlux(neededEC * 10);
                activated = true;
                part.force_activate();
                opFloor = FindAcceptableAltitude(); // Keep updating tooltip display. Also, needed EC.
                SetFieldsEventsActions(true);
                log.Info("EC Activation charge at " + neededEC + "(" + FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude + "/" + gLimitEff + ")");
                PlayAnimation(1f);
            }
            else
                log.Warning("Can only initialize when shut down!");
        }

        [KSPEvent(name = "BeaconShutdown", active = false, guiActive = true, guiName = "Shutdown")]
        public void BeaconShutdown()
        {
            if (activated)
            {
                activated = false;
                SetFieldsEventsActions(false);
                PlayAnimation(-1f);
            }
            else
                log.Warning("Can only shut down when activated!");
        }

        private void PlayAnimation(float speed)
        {
            if (anim == null) return;
            anim[animationName].normalizedSpeed = speed;
            anim.Play(animationName);
        }

        private void SetFieldsEventsActions(bool activated)
        {
            beaconStatus = activated ? "Active" : "Offline";
            Fields["neededEC"].guiActive = !activated;
            Fields["constantEC"].guiActive = activated;
            Events["BeaconInitialize"].active = !activated;
            Events["BeaconShutdown"].active = activated;
            Actions["BeaconInitializeAction"].active = !activated;
            Actions["BeaconShutdownAction"].active = activated;
        }

        [KSPAction("Toggle Beacon")]
        public void ToggleBeaconAction(KSPActionParam param)
        {
            if (activated)
                BeaconShutdown();
            else
                BeaconInitialize();
        }
        [KSPAction("Initialize Beacon")]
        public void BeaconInitializeAction(KSPActionParam param)
        {
            if (!activated)
                BeaconInitialize();
            else
                log.Warning("Can only initialize when shut down!");
        }
        [KSPAction("Shutdown Beacon")]
        public void BeaconShutdownAction(KSPActionParam param)
        {
            if (activated)
                BeaconShutdown();
            else
                log.Warning("Can only shut down when activated!");
        }

        public override string GetInfo()
        {
            StringBuilder info = new StringBuilder();

            info.AppendLine("Beacon Model: " + beaconModel);
            switch (beaconModel)
            {
                case ("LB10"):
                    info.AppendLine("Best for distances below 1Gm.");
                    break;
                case ("LB15"):
                    info.AppendLine("Best for masses below 60T and distances between 100Mm and 100Gm.");
                    break;
                case ("LB100"):
                    info.AppendLine("Best for distances above 1Gm.");
                    break;
            }
            info.AppendLine("Gravity limit: " + gLimit + "g");

            if (builtInHCU || canJumpSelf || (!jumpTargetable) || (distPenalty > 0))
                info.AppendLine();
            if (builtInHCU)
                info.AppendLine("<b><color=#99ff00ff>Contains a built-in HCU</color></b>");
            if (canJumpSelf)
                info.AppendLine("<color=#99ff00ff>Can self-transfer.</color>");
            if (!jumpTargetable)
                info.AppendLine("<b><color=#FDA401>Cannot be a target beacon</color></b>");
            if (distPenalty > 0)
                info.AppendFormat("<color=#FDA401>Cost prohibitive beyond {0} km</color>", distPenalty / 1000).AppendLine();

            if (jumpResources.Count() > 0)
                info.AppendLine().AppendLine("<b><color=#99ff00ff>Requires:</color></b>");
            foreach (ESLDJumpResource Jresource in jumpResources)
                info.AppendFormat("<b>{0}:</b> Ratio: {1}", Jresource.name, Jresource.ratio.ToString("F2")).AppendLine();
            return info.ToString().TrimEnd(Environment.NewLine.ToCharArray());
        }

        public void OnBeforeSerialize()
        {
            serializedJumpResources.Clear();
            foreach(ESLDJumpResource resource in jumpResources)
            {
                serializedJumpResources.Add(SerializationHelper.SerializeObject(resource));
            }
        }

        public void OnAfterDeserialize()
        {
            jumpResources.Clear();
            foreach(string resource in serializedJumpResources)
            {
                jumpResources.Add(SerializationHelper.DeserializeObject<ESLDJumpResource>(resource));
            }
            serializedJumpResources.Clear();
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

    [Serializable]
    public class ESLDJumpResource : IConfigNode
    {
        [SerializeField]
        public string name;
        public int resID;
        [SerializeField]
        public float ratio = 1;
        [SerializeField]
        public double fuelOnBoard = 0;
        [SerializeField]
        public float ECMult = 1;
        [SerializeField]
        public float minEC = 0;
        public bool fuelCheck = false;
        [SerializeField]
        public bool neededToBoot = true;

        public static Dictionary<string, float> HEResources = new Dictionary<string, float>()
        {
            {"Karborundum", 1}
        };

        public ESLDJumpResource() { }
        public ESLDJumpResource(string name, float ratio=1, double fuelOnBoard = 0, bool fuelCheck = false, float ECMult = 1, float minEC = 0, bool neededToBoot = true)
        {
            this.name = name;
            this.ratio = ratio;
            this.fuelOnBoard = fuelOnBoard;
            if (fuelOnBoard != 0)
                this.fuelCheck = true;
            else
                this.fuelCheck = fuelCheck;
            if (ECMult == 1 && HEResources.ContainsKey(this.name))
                this.ECMult = HEResources[this.name];
            resID = PartResourceLibrary.Instance.GetDefinition(this.name).id;
        }

        public void Load(ConfigNode node)
        {
            name = node.GetValue("name");
            node.TryGetValue("ratio", ref ratio);
            fuelCheck = node.TryGetValue("fuelOnBoard", ref fuelOnBoard);
            if (!node.TryGetValue("ECMult", ref ECMult) && HEResources.ContainsKey(name))
                ECMult = HEResources[name];
            node.TryGetValue("minEC", ref minEC);
            node.TryGetValue("neededToBoot", ref neededToBoot);
            resID = PartResourceLibrary.Instance.GetDefinition(this.name).id;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue("name", name);
            node.AddValue("fuelOnBoard", fuelOnBoard);
        }

        public double GetFuelOnBoard(Part beaconPart)
        {
            fuelOnBoard = 0;
            if (beaconPart == null)
                return 0;
            beaconPart.crossfeedPartSet.GetConnectedResourceTotals(resID, out fuelOnBoard, out _, true);

            return fuelOnBoard;
        }
    }
}
