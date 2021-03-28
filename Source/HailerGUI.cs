using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ESLDCore
{
    public class HailerGUI : MonoBehaviour
    {
        public static Dictionary<string, float> highEnergyResources = new Dictionary<string, float>()
        {
            // CRP/USI Curated Resources
            {"Karborundum", 1}, {"Uraninite", 0.8f},
            // NFT Curated Resources
            {"DepletedUranium", 0.3f}, {"EnrichedUranium", 0.7f},
            // KSPI-E Curated Resources
            {"Actinides", 0.6f}, {"Antimatter", 1}, {"ChargedParticles", 1}, {"DepletedFuel", 0.3f},
            {"ExoticMatter", 1}, {"Fluorine", 0.4f}, {"LqdHe3", 0.6f}, {"LqdTritium", 0.8f},
            {"Plutonium-238", 0.8f}, {"ThF4", 1}, {"UraniumNitride", 0.65f}, {"VacuumPlasma", 1},
            // Possibly Deprecated Resources
            {"Uranium", 0.7f}, {"Thorium", 0.6f}
        };

        public static List<HailerGUI> openWindows = new List<HailerGUI>();

        public Vessel vessel = null;
        public ESLDBeacon nearBeacon;
        public List<ESLDHailer> hailers = new List<ESLDHailer>();
        private List<ESLDBeacon> nearBeacons = new List<ESLDBeacon>();
        private static Dictionary<Vessel, List<ESLDBeacon>> farBeaconVessels = new Dictionary<Vessel, List<ESLDBeacon>>();
        private static Dictionary<Vessel, List<ModuleESLDLens>> lensVessels = new Dictionary<Vessel, List<ModuleESLDLens>>();
        private static List<ModuleESLDLens> farLenses = new List<ModuleESLDLens>();
        private static List<PartModule> createdModules = new List<PartModule>();
        private TargetDetails selectedTarget;
        private int currentBeaconIndex = -1;

        public static List<ModuleESLDLens> Lenses
        {
            get => farLenses;
        }

        protected Rect window;

        public GameObject predictionGameObject = null;
        public OrbitDriver predictionOrbitDriver = null;
        public OrbitRenderer predictionOrbitRenderer = null;
        PatchedConicSolver predictionPatchedConicSolver;
        PatchedConicRenderer predictionPatchedConicRenderer;
        bool predictionsDrawn = false;
        public bool wasInMapView;
        
        public static GUIStyle buttonNeutral;
        public static GUIStyle labelHasFuel;
        public static GUIStyle labelNoFuel;

        public static GUIStyle buttonHasFuel;
        public static GUIStyle buttonNoFuel;
        public static GUIStyle buttonNoPath;

        public DisplayMode displayMode = DisplayMode.Selection;

        private float driftpenalty;
        private List<TargetDetails> targetDetails = new List<TargetDetails>();
        float tonnage;
        float sciBonus;
        Dictionary<Part, string> HCUParts;
        List<Part> HCUPartsList = new List<Part>();
        List<string> HCUPartFailures = new List<string>();

        public enum DisplayMode
        {
            Selection,
            Confirmation
        }

        public void OnGUI()
        {
            SetupStyles();
            if ((window.x == 0) && (window.y == 0))
            {
                window = new Rect(Screen.width / 2, Screen.height / 2, 10, 10);
            }
            switch (displayMode)
            {
                case DisplayMode.Selection:
                    window = GUILayout.Window(this.GetInstanceID(), window, WindowInterface, "Warp Information", HighLogic.Skin.window, GUILayout.MinWidth(400), GUILayout.MinHeight(200));
                    break;
                case DisplayMode.Confirmation:
                    window = GUILayout.Window(this.GetInstanceID(), window, WindowInterface, "Warp Information", HighLogic.Skin.window, GUILayout.MinWidth(400), GUILayout.MinHeight(200));
                    break;
            }
        }

        public void WindowInterface(int GuiId)
        {
            GUILayout.BeginVertical();
            switch (displayMode)
            {
                case DisplayMode.Selection:
                    if (nearBeacons.Count <= 0 || nearBeacon == null)
                        GUILayout.Label("No active beacons found.");
                    else
                    {
                        if (driftpenalty > 0) GUILayout.Label(String.Format("+{0:F2}% due to Drift.", driftpenalty));
                        if (nearBeacon.UnsafeTransfer)
                        {
                            if (vessel.GetCrewCount() > 0 || HCUPartFailures.Count > 0) GUILayout.Label("WARNING: This beacon has no active Heisenkerb Compensator.");
                            if (vessel.GetCrewCount() > 0) GUILayout.Label("Transfer will kill crew.");
                            if (HCUPartFailures.Count > 0) GUILayout.Label("Some resources will destabilize.");
                        }
                        if (targetDetails.Count == 0)
                            GUILayout.Label("No active target beacons found.");
                        for (int i = targetDetails.Count - 1; i >= 0; i--)
                        {
                            GUIStyle fuelstate = targetDetails[i].affordable ? (targetDetails[i].pathCheck.clear ? buttonHasFuel : buttonNoPath) : buttonNoFuel;
                            if (GUILayout.Button(String.Format("{0} ({1}, {2:F1}km) | {3:F2}", targetDetails[i].vesselName, targetDetails[i].targetSOI, targetDetails[i].targetAlt, targetDetails[i].tripCost), fuelstate))
                            {
                                if (targetDetails[i].affordable && targetDetails[i].pathCheck.clear)
                                {
                                    selectedTarget = targetDetails[i];
                                    if (nearBeacon.CarriesVelocity) ShowExitOrbit(vessel, selectedTarget.targetVessel);
                                    displayMode = DisplayMode.Confirmation;
                                }
                                else
                                {
                                    string messageToPost = "";
                                    if (!targetDetails[i].affordable)
                                    {
                                        int index = nearBeacon.JumpResources.FindIndex(res => res.ratio * targetDetails[i].tripCost > res.fuelOnBoard);
                                        if (index < 0)
                                            messageToPost = "Index error.";
                                        else
                                            messageToPost = String.Format("Cannot Warp: Origin beacon has {0:F2} of {1:F2} {2} required to warp.", nearBeacon.JumpResources[index].fuelOnBoard, nearBeacon.JumpResources[index].ratio * targetDetails[i].tripCost, nearBeacon.JumpResources[index].name);
                                    }
                                    else if (!targetDetails[i].pathCheck.clear)
                                    {
                                        string thevar = (targetDetails[i].pathCheck.blockingBody.name == "Mun" || targetDetails[i].pathCheck.blockingBody.name == "Sun") ? "the " : "";
                                        switch (targetDetails[i].pathCheck.blockReason)
                                        {
                                            case "Gravity":
                                                messageToPost = String.Format("Cannot Warp: Path of transfer intersects a high-gravity area around {1}{0}.", targetDetails[i].pathCheck.blockingBody.name, thevar);
                                                break;
                                            case "Proximity":
                                                messageToPost = String.Format("Cannot Warp: Path of transfer passes too close to {1}{0}.",targetDetails[i].pathCheck.blockingBody.name, thevar);
                                                break;
                                            case "Null":
                                                messageToPost = "Cannot Warp: No near beacon assigned. This is an error.";
                                                break;
                                            default:
                                                messageToPost = "Cannot Warp: Path is blocked.";
                                                break;
                                        }
                                    }
                                    ScreenMessages.PostScreenMessage(messageToPost, 5.0f, ScreenMessageStyle.UPPER_CENTER);
                                }
                            }
                        }
                        GUILayout.FlexibleSpace();
                        if (nearBeacons.Count > 1)
                        {
                            GUILayout.Label(String.Format("Current Beacon: {0} ({1})", nearBeacon.Description, nearBeacon.Vessel.vesselName));
                            if (GUILayout.Button(String.Format("Next Beacon ({0} of {1})", (currentBeaconIndex + 1), nearBeacons.Count), buttonNeutral))
                            {
                                if (currentBeaconIndex + 1 < nearBeacons.Count)
                                    nearBeacon = nearBeacons[currentBeaconIndex + 1];
                                else
                                    nearBeacon = nearBeacons[0];
                            }
                        }
                        if (GUILayout.Button("Close Beacon Interface", buttonNeutral))
                            Destroy(this);
                    }
                    break;
                case DisplayMode.Confirmation:
                    if (driftpenalty > 0) GUILayout.Label(String.Format("+{0:F2}% due to Drift.", driftpenalty));
                    if (nearBeacon.UnsafeTransfer)
                    {
                        if (vessel.GetCrewCount() > 0 || HCUPartFailures.Count > 0) GUILayout.Label("WARNING: This beacon has no active Heisenkerb Compensator.", labelNoFuel);
                        if (vessel.GetCrewCount() > 0) GUILayout.Label("Transfer will kill crew.", labelNoFuel);
                        if (HCUPartFailures.Count > 0)
                        {
                            GUILayout.Label("These resources will destabilize in transit:", labelNoFuel);
                            for (int i = 0; i < HCUPartFailures.Count; i++)
                            {
                                GUILayout.Label(HCUPartFailures[i], labelNoFuel);
                            }
                        }
                    }
                    GUILayout.Label("Confirm Warp:");
                    string costLabel = "Base cost: ";
                    for (int i = 0; i < nearBeacon.JumpResources.Count; i++)
                        costLabel += String.Format("{0:F2} {1}{2}", selectedTarget.tripCost * nearBeacon.JumpResources[i].ratio, nearBeacon.JumpResources[i].name, i + 1 < nearBeacon.JumpResources.Count ? ", " : "");
                    GUILayout.Label(costLabel + ".");
                    List<string> modifiers = nearBeacon.GetCostModifiers(vessel, selectedTarget.targetVessel, tonnage, HCUParts.Keys.ToList());
                    for (int i = 0; i < modifiers.Count; i++)
                        GUILayout.Label(modifiers[i]);
                    GUILayout.Label(String.Format("Destination: {0} at {1:F1}km.", selectedTarget.targetVessel.mainBody.name, selectedTarget.targetAlt));

                    GUILayout.Label(String.Format("Transfer will emerge within {0:N0}m of destination beacon.", selectedTarget.precision));
                    if (selectedTarget.targetVessel.altitude - selectedTarget.precision <= selectedTarget.targetVessel.mainBody.Radius * 0.1f || selectedTarget.targetVessel.altitude - selectedTarget.precision <= selectedTarget.targetVessel.mainBody.atmosphereDepth)
                    {
                        GUILayout.Label(String.Format("Arrival area is very close to {0}.", selectedTarget.targetVessel.mainBody.name), labelNoFuel);
                    }
                    if (nearBeacon.CarriesVelocity)
                    {
                        Vector3d transferVelOffset = ESLDBeacon.GetJumpVelOffset(vessel, selectedTarget.targetVessel) - selectedTarget.targetVessel.orbit.vel;
                        GUILayout.Label(String.Format("Velocity relative to exit beacon will be {0:F0}m/s.", transferVelOffset.magnitude));
                    }
                    if (selectedTarget.returnFuelCheck && selectedTarget.returnBeacon != null)
                    {
                        string fuelMessage;
                        if (selectedTarget.returnAffordable)
                            fuelMessage = "Destination beacon can make return trip using ";
                        else
                            fuelMessage = "Destination beacon would need ";

                        string fuelCount = "Destination beacon has ";
                        for (int i = 0; i < selectedTarget.returnBeacon.JumpResources.Count; i++)
                            fuelCount += String.Format("{0:F0} {1}{2}", selectedTarget.returnBeacon.JumpResources[i].fuelOnBoard, selectedTarget.returnBeacon.JumpResources[i].name, i + 1 < selectedTarget.returnBeacon.JumpResources.Count ? ", " : "");

                        for (int i = 0; i < selectedTarget.returnBeacon.JumpResources.Count; i++)
                            fuelMessage += String.Format("{0:F2} {1}{2}", selectedTarget.returnCost * selectedTarget.returnBeacon.JumpResources[i].ratio, selectedTarget.returnBeacon.JumpResources[i].name, i + 1 < selectedTarget.returnBeacon.JumpResources.Count ? ", " : "");

                        GUILayout.Label(fuelCount + ".");
                        GUILayout.Label(fuelMessage + (selectedTarget.returnAffordable ? "." : " for return trip using active beacons."), selectedTarget.returnAffordable ? labelHasFuel : labelNoFuel);
                    }
                    else
                    {
                        GUILayout.Label("Destination beacon's fuel could not be checked.");
                    }
                    if (GUILayout.Button("Confirm and Warp", buttonNeutral))
                    {
                        if (selectedTarget.pathCheck.clear)
                        {
                            HideExitOrbit();
                            nearBeacon.Warp(vessel, selectedTarget.targetVessel, selectedTarget.precision, HCUPartsList);
                            Destroy(this);
                        }
                        else
                        {
                            ScreenMessages.PostScreenMessage("Jump Failed!  Transfer path has become obstructed.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                    if (GUILayout.Button("Back", buttonNeutral))
                    {
                        displayMode = DisplayMode.Selection;
                        window.height = 0;
                    }
                    break;
            }
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        public void Update()
        {
            UpdateNearBeacons();
            
            if (nearBeacon == null || nearBeacon.Vessel != vessel)
            {
                bool present = nearBeacon != null;
                for (int i = hailers.Count - 1; i >= 0; i--)
                {
                    bool hailerActive = hailers[i].hailerActive;
                    hailers[i].Fields["nearBeaconDistance"].guiActive = present && hailerActive;
                    hailers[i].Fields["nearBeaconRelVel"].guiActive = present && hailerActive;
                    hailers[i].Fields["hasNearBeacon"].guiActive =  hailerActive;
                    hailers[i].hasNearBeacon = present ? "Present" : "Not Present";
                }
            }
            else
            {
                for (int i = hailers.Count - 1; i >= 0; i--)
                {
                    hailers[i].Fields["nearBeaconDistance"].guiActive = false;
                    hailers[i].Fields["nearBeaconRelVel"].guiActive = false;
                    hailers[i].Fields["hasNearBeacon"].guiActive =  hailers[i].hailerActive;
                    hailers[i].hasNearBeacon = "Onboard";
                }
            }
            if (nearBeacon == null)
                return;

            float nearBeaconDistance = Vector3.Distance(vessel.GetWorldPos3D(), nearBeacon.Vessel.GetWorldPos3D());
            float nearBeaconRelVel = Vector3.Magnitude(vessel.obt_velocity - nearBeacon.Vessel.obt_velocity);
            driftpenalty = ESLDBeacon.GetDriftPenalty(nearBeaconDistance, nearBeaconRelVel, nearBeacon.GetCrewBonuses("Pilot", 0.5f, 5));
            HCUParts = GetHCUParts(vessel);
            //[Part name] - [Resource name]
            HCUPartFailures.Clear();
            HCUPartFailures.AddRange(HCUParts.Select(kvp => kvp.Key.name + " - " + kvp.Value));
            HCUPartsList = HCUParts.Keys.ToList();
            tonnage = vessel.GetTotalMass();
            sciBonus = nearBeacon.GetCrewBonuses("Scientist", 0.5f, 5);
            
            for (int i = targetDetails.Count - 1; i >= 0; i--)
                targetDetails[i].Update();

            for (int i = hailers.Count - 1; i >= 0; i--)
            {
                hailers[i].nearBeaconDistance = nearBeaconDistance;
                hailers[i].nearBeaconRelVel = nearBeaconRelVel;
            }
        }

        public void UpdateNearBeacons()
        {
            nearBeacons.Clear();
            for (int i = FlightGlobals.VesselsLoaded.Count - 1; i >= 0; i--)
            {
                List<ESLDBeacon> beaconsOnVessel = FlightGlobals.VesselsLoaded[i].FindPartModulesImplementing<ESLDBeacon>();
                for (int j = beaconsOnVessel.Count - 1; j >= 0; j--)
                {
                    if (beaconsOnVessel[j].activated && beaconsOnVessel[j].moduleIsEnabled && (FlightGlobals.VesselsLoaded[i] != vessel || beaconsOnVessel[j].canJumpSelf))
                        nearBeacons.Add(beaconsOnVessel[j]);
                }
            }
            if (nearBeacons.Count == 0)
            {
                nearBeacon = null;
                currentBeaconIndex = -1;
                displayMode = DisplayMode.Selection;
            }
            else
            {
                nearBeacons.OrderBy(b => Vector3.SqrMagnitude(b.Vessel.GetWorldPos3D() - vessel.GetWorldPos3D()));
                currentBeaconIndex = nearBeacons.IndexOf(nearBeacon);
                if (currentBeaconIndex < 0)
                {
                    nearBeacon = nearBeacons[0];
                    currentBeaconIndex = 0;
                    displayMode = DisplayMode.Selection;
                }
            }
        }

        public static List<ESLDBeacon> QueryVesselFarBeacons(ProtoVessel vesselToQuery)
        {
            List<ESLDBeacon> beacons = new List<ESLDBeacon>();
            List<ProtoPartSnapshot> parts = vesselToQuery.protoPartSnapshots;
            int partCount = parts.Count;
            for (int i = 0; i < partCount; i++)
            {
                List<ProtoPartModuleSnapshot> modules = parts[i].modules.FindAll(m => m.moduleName == "ESLDBeacon");
                int moduleCount = modules.Count;
                for (int j = 0; j < moduleCount; j++)
                {
                    ESLDBeacon beacon = (ESLDBeacon)modules[j].moduleRef;
                    if (beacon == null)
                    {
                        beacon = ESLDBeacon.InstantiateFromSnapshot(parts[i], modules[j], j);
                        createdModules.Add(beacon);
                    }

                    if (beacon.activated && beacon.moduleIsEnabled && beacon.jumpTargetable)
                        beacons.Add(beacon);
                }
            }
            return beacons;
        }

        public static List<ModuleESLDLens> QueryVesselLenses(ProtoVessel vesselToQuery)
        {
            List<ModuleESLDLens> lenses = new List<ModuleESLDLens>();
            List<ProtoPartSnapshot> parts = vesselToQuery.protoPartSnapshots;
            int partCount = parts.Count;
            for (int i = 0; i < partCount; i++)
            {
                List<ProtoPartModuleSnapshot> modules = parts[i].modules.FindAll(m => m.moduleName == "ModuleESLDLens");
                int moduleCount = modules.Count;
                for (int j = 0; j < moduleCount; j++)
                {
                    ModuleESLDLens lens = (ModuleESLDLens)modules[j].moduleRef;
                    if (lens == null)
                    {
                        lens = ModuleESLDLens.InstantiateFromSnapshot(parts[i], modules[j], j);
                        createdModules.Add(lens);
                    }

                    if (lens.activated && lens.moduleIsEnabled)
                        lenses.Add(lens);
                }
            }
            return lenses;
        }

        public static HailerGUI ActivateGUI(Vessel hostVessel)
        {
            HailerGUI hailerGUI = openWindows.FirstOrDefault(gui => gui.vessel == hostVessel);
            if (hailerGUI != null)
                return hailerGUI;
            hailerGUI = hostVessel.gameObject.AddComponent<HailerGUI>();
            hailerGUI.vessel = hostVessel;
            return hailerGUI;
        }

        public static void CloseGUI(Vessel hostVessel)
        {
            HailerGUI hailerGUI = openWindows.FirstOrDefault(gui => gui.vessel == hostVessel);
            if (hailerGUI != null)
                Destroy(hailerGUI);
        }

        public static void CloseAllGUIs()
        {
            for (int i = openWindows.Count - 1; i >= 0; i--)
                Destroy(openWindows[i]);
        }

        public void Awake()
        {
            openWindows.Add(this);
            GameEvents.onVesselWasModified.Add(VesselWasModified);
            GameEvents.onVesselLoaded.Add(VesselLoaded);
            GameEvents.onVesselUnloaded.Add(VesselUnloaded);
            GameEvents.onNewVesselCreated.Add(VesselCreated);
            GameEvents.onVesselCreate.Add(VesselCreated);
            GameEvents.onVesselWillDestroy.Add(VesselDestroyed);
        }

        public void Start()
        {
            hailers = vessel.FindPartModulesImplementing<ESLDHailer>();
            for (int i = hailers.Count - 1; i >= 0; i--)
                hailers[i].AttachedGui = this;

            foreach (Vessel queryVessel in FlightGlobals.VesselsUnloaded)
            {
                if (!farBeaconVessels.ContainsKey(queryVessel))
                {
                    farBeaconVessels.Add(queryVessel, QueryVesselFarBeacons(queryVessel.protoVessel));
                }
                if (farBeaconVessels[queryVessel].Count > 0)
                    targetDetails.Add(new TargetDetails(queryVessel, this));
                if (!lensVessels.ContainsKey(queryVessel))
                {
                    lensVessels.Add(queryVessel, QueryVesselLenses(queryVessel.protoVessel));
                    farLenses.AddRange(lensVessels[queryVessel]);
                }
            }
        }

        public void OnDestroy()
        {
            for (int i = hailers.Count - 1; i >=0; i--)
                hailers[i].AttachedGui = null;

            GameEvents.onVesselWasModified.Remove(VesselWasModified);
            GameEvents.onVesselLoaded.Remove(VesselLoaded);
            GameEvents.onVesselUnloaded.Remove(VesselUnloaded);
            GameEvents.onNewVesselCreated.Remove(VesselCreated);
            GameEvents.onVesselCreate.Remove(VesselCreated);
            GameEvents.onVesselWillDestroy.Remove(VesselDestroyed);

            if (predictionGameObject != null)
                Destroy(predictionGameObject);

            openWindows.Remove(this);
            if (openWindows.Count == 0)
            {
                // With no open windows, we're about to lose visibility and control of these static lists.
                // Better to clear them and rebuild than try to cache them.
                foreach (PartModule module in createdModules)
                {
                    Destroy(module);
                }
                createdModules.Clear();
                farBeaconVessels.Clear();
                farLenses.Clear();
                lensVessels.Clear();

                HailerButton.Instance.button.SetFalse(false);
            }
        }

        public static void RemoveFarVessel(Vessel vessel)
        {
            if (farBeaconVessels.ContainsKey(vessel))
            {
                for (int i = farBeaconVessels[vessel].Count - 1; i >= 0; i--)
                {
                    if (createdModules.Contains(farBeaconVessels[vessel][i]))
                    {
                        Destroy(farBeaconVessels[vessel][i]);
                        createdModules.Remove(farBeaconVessels[vessel][i]);
                    }
                }
                farBeaconVessels.Remove(vessel);
            }

            if (lensVessels.ContainsKey(vessel))
            {
                for (int i = lensVessels[vessel].Count - 1; i >= 0; i--)
                {
                    farLenses.Remove(lensVessels[vessel][i]);
                    if (createdModules.Contains(lensVessels[vessel][i]))
                    {
                        Destroy(lensVessels[vessel][i]);
                        createdModules.Remove(lensVessels[vessel][i]);
                    }
                }
                lensVessels.Remove(vessel);
            }
        }

        public void VesselDestroyed(Vessel vessel)
        {
            targetDetails.RemoveAll(tgt => tgt.targetVessel == vessel);
            RemoveFarVessel(vessel);
        }

        public void VesselUnloaded(Vessel vessel)
        {
            if (vessel == this.vessel)
                return;
            if (!farBeaconVessels.ContainsKey(vessel))
            {
                farBeaconVessels.Add(vessel, QueryVesselFarBeacons(vessel.protoVessel));
            }
            if (farBeaconVessels[vessel].Count > 0)
                targetDetails.Add(new TargetDetails(vessel, this));
            if (!lensVessels.ContainsKey(vessel))
            {
                lensVessels.Add(vessel, QueryVesselLenses(vessel.protoVessel));
                farLenses.AddRange(lensVessels[vessel]);
            }
        }

        public void VesselLoaded(Vessel vessel)
        {
            targetDetails.RemoveAll(tgt => tgt.targetVessel == vessel);
            RemoveFarVessel(vessel);
        }

        public void VesselCreated(Vessel vessel)
        {
            if (!vessel.loaded)
            {
                if (!farBeaconVessels.ContainsKey(vessel))
                {
                    farBeaconVessels.Add(vessel, QueryVesselFarBeacons(vessel.protoVessel));
                }
                if (farBeaconVessels[vessel].Count > 0)
                    targetDetails.Add(new TargetDetails(vessel, this));
                if (!lensVessels.ContainsKey(vessel))
                {
                    lensVessels.Add(vessel, QueryVesselLenses(vessel.protoVessel));
                    farLenses.AddRange(lensVessels[vessel]);
                }
            }
        }

        public void VesselWasModified(Vessel vessel)
        {
            if (vessel != this.vessel)
                return;
            for (int i = hailers.Count - 1; i >= 0; i--)
            {
                //hailers[i].
            }
            hailers = this.vessel.FindPartModulesImplementing<ESLDHailer>();
            // Do I really care if a vessel doesn't have a hailer?
            if (hailers.Count == 0)
                Destroy(this);
        }

        // Show exit orbital predictions
        private void ShowExitOrbit(Vessel nearObject, Vessel farObject)
        {
            // Recenter map, save previous state.
            wasInMapView = MapView.MapIsEnabled;
            if (!MapView.MapIsEnabled) MapView.EnterMapView();
            //log.Debug("Finding target.");
            MapObject farTarget = FindVesselBody(farObject);
            if (farTarget != null) MapView.MapCamera.SetTarget(farTarget);
            Vector3 mapCamPos = ScaledSpace.ScaledToLocalSpace(MapView.MapCamera.transform.position);
            Vector3 farTarPos = ScaledSpace.ScaledToLocalSpace(farTarget.transform.position);
            float dirScalar = Vector3.Distance(mapCamPos, farTarPos);
            //log.Debug("Initializing, camera distance is " + dirScalar);

            // Initialize projection stuff.
            if (!IsPatchedConicsAvailable)
            {
                HideExitOrbit();
                return;
            }
            predictionsDrawn = true;

            //log.Debug("Beginning orbital projection.");
            Vector3d exitTraj = ESLDBeacon.GetJumpVelOffset(nearObject, farObject);
            if (predictionGameObject != null) Destroy(predictionGameObject);
            predictionGameObject = new GameObject("OrbitRendererGameObject");
            predictionOrbitDriver = predictionGameObject.AddComponent<OrbitDriver>();
            predictionOrbitDriver.orbit.referenceBody = farObject.mainBody;
            predictionOrbitDriver.orbit = new Orbit();
            predictionOrbitDriver.referenceBody = farObject.mainBody;
            predictionOrbitDriver.upperCamVsSmaRatio = 999999;  // Took forever to figure this out - this sets at what zoom level the orbit appears.  Was causing it not to appear at small bodies.
            predictionOrbitDriver.lowerCamVsSmaRatio = 0.0001f;
            predictionOrbitDriver.orbit.UpdateFromStateVectors(farObject.orbit.pos, exitTraj, farObject.mainBody, Planetarium.GetUniversalTime());
            predictionOrbitDriver.orbit.Init();
            Vector3d p = predictionOrbitDriver.orbit.getRelativePositionAtUT(Planetarium.GetUniversalTime());
            Vector3d v = predictionOrbitDriver.orbit.getOrbitalVelocityAtUT(Planetarium.GetUniversalTime());
            predictionOrbitDriver.orbit.h = Vector3d.Cross(p, v);
            predictionOrbitDriver.updateMode = OrbitDriver.UpdateMode.TRACK_Phys;
            predictionOrbitDriver.orbitColor = Color.red;
            //log.Debug("Displaying orbital projection.");
            predictionOrbitRenderer = predictionGameObject.AddComponent<OrbitRenderer>();
            predictionOrbitRenderer.SetColor(Color.red);
            predictionOrbitRenderer.vessel = this.vessel;
            predictionOrbitDriver.vessel = this.vessel;
            predictionOrbitRenderer.upperCamVsSmaRatio = 999999;
            predictionOrbitRenderer.lowerCamVsSmaRatio = 0.0001f;
            predictionOrbitRenderer.celestialBody = farObject.mainBody;
            predictionOrbitRenderer.driver = predictionOrbitDriver;
            predictionOrbitDriver.Renderer = predictionOrbitRenderer;

            if (true)
            {
                // This draws the full Patched Conics prediction.
                predictionPatchedConicSolver = predictionGameObject.AddComponent<PatchedConicSolver>();
                predictionPatchedConicRenderer = predictionGameObject.AddComponent<PatchedConicRenderer>();
                predictionOrbitRenderer.drawIcons = OrbitRendererBase.DrawIcons.NONE;
                predictionOrbitRenderer.drawMode = OrbitRendererBase.DrawMode.OFF;
            }
            else
            {
                // This draws just the first patch, similar to a Level 1 tracking station.
                predictionOrbitRenderer.driver.drawOrbit = true;
                predictionOrbitRenderer.drawIcons = OrbitRenderer.DrawIcons.OBJ_PE_AP;
                predictionOrbitRenderer.drawMode = OrbitRenderer.DrawMode.REDRAW_AND_RECALCULATE;
                predictionOrbitRenderer.enabled = true;
            }
            this.StartCoroutine(NullOrbitDriverVessels());
            // Splash some color on it.

            // Directional indicator.
            /*
            float baseWidth = 20.0f;
            double baseStart = 10;
            double baseEnd = 50;
            oDirObj = new GameObject("Indicator");
            oDirObj.layer = 10; // Map layer!
            oDirection = oDirObj.AddComponent<LineRenderer>();
            oDirection.useWorldSpace = false;
            oOrigin = null;
            foreach (Transform sstr in ScaledSpace.Instance.scaledSpaceTransforms)
            {
                if (sstr.name == far.mainBody.name)
                {
                    oOrigin = sstr;
                    log.debug("Found origin: " + sstr.name);
                    break;
                }
            }
            oDirection.transform.parent = oOrigin;
            oDirection.transform.position = ScaledSpace.LocalToScaledSpace(far.transform.position);
            oDirection.material = new Material(Shader.Find("Particles/Additive"));
            oDirection.SetColors(Color.clear, Color.red);
            if (dirScalar / 325000 > baseWidth) baseWidth = dirScalar / 325000f;
            oDirection.SetWidth(baseWidth, 0.01f);
            log.debug("Base Width set to " + baseWidth);
            oDirection.SetVertexCount(2);
            if (dirScalar / 650000 > baseStart) baseStart = dirScalar / 650000;
            if (dirScalar / 130000 > baseEnd) baseEnd = dirScalar / 130000;
            log.debug("Base Start set to " + baseStart);
            log.debug("Base End set to " + baseEnd);
            oDirection.SetPosition(0, Vector3d.zero + exitTraj.xzy.normalized * baseStart);
            oDirection.SetPosition(1, exitTraj.xzy.normalized * baseEnd);
            oDirection.enabled = true;
             */
        }

        public System.Collections.IEnumerator NullOrbitDriverVessels()
        {
            // Initially tried letting it keep this vessel, but things flicker.
            // Then, tried it as null all the time, but it throws a NullRef in PatchedConicRenderer.Start()
            // But, once PCR is initialized, it's all good to set the OrbitDriver.vessel to null.
            if (predictionPatchedConicRenderer.relativeTo == null)
                yield return new WaitForEndOfFrame();
            while (predictionPatchedConicRenderer.relativeTo == null)
            {
                if (!HighLogic.LoadedSceneIsFlight)
                {
                    //log.Debug("Scene changed, breaking Coroutine.");
                    yield break;
                }
                yield return null;
            }
            predictionOrbitRenderer.vessel = null;
            predictionOrbitDriver.vessel = null;
        }

        // Update said predictions
        private void UpdateExitOrbit(Vessel nearObject, Vessel farObject)
        {
            if (!predictionsDrawn)
                return;
            if (!IsPatchedConicsAvailable)
            {
                HideExitOrbit();
                return;
            }

            Vector3 mapCamPos = ScaledSpace.ScaledToLocalSpace(MapView.MapCamera.transform.position);
            MapObject farTarget = MapView.MapCamera.target;
            Vector3 farTarPos = ScaledSpace.ScaledToLocalSpace(farTarget.transform.position);
            float dirScalar = Vector3.Distance(mapCamPos, farTarPos);
            Vector3d exitTraj = ESLDBeacon.GetJumpVelOffset(nearObject, farObject);
            predictionOrbitRenderer.driver.referenceBody = farObject.mainBody;
            predictionOrbitRenderer.driver.orbit.referenceBody = farObject.mainBody;
            predictionOrbitRenderer.driver.pos = farObject.orbit.pos;
            predictionOrbitRenderer.celestialBody = farObject.mainBody;
            predictionOrbitRenderer.SetColor(Color.red);
            predictionOrbitDriver.orbit.UpdateFromStateVectors(farObject.orbit.pos, exitTraj, farObject.mainBody, Planetarium.GetUniversalTime());

            /* Direction indicator is broken/not required
            float baseWidth = 20.0f;
            double baseStart = 10;
            double baseEnd = 50;
            oDirection.transform.position = ScaledSpace.LocalToScaledSpace(far.transform.position);
            if (dirScalar / 325000 > baseWidth) baseWidth = dirScalar / 325000f;
            oDirection.SetWidth(baseWidth, 0.01f);
            if (dirScalar / 650000 > baseStart) baseStart = dirScalar / 650000;
            if (dirScalar / 130000 > baseEnd) baseEnd = dirScalar / 130000;
//          log.debug("Camera distance is " + dirScalar + " results: " + baseWidth + " " + baseStart + " " + baseEnd);
            oDirection.SetPosition(0, Vector3d.zero + exitTraj.xzy.normalized * baseStart);
            oDirection.SetPosition(1, exitTraj.xzy.normalized * baseEnd);
            oDirection.transform.eulerAngles = Vector3d.zero;*/
        }

        // Back out of orbital predictions.
        private void HideExitOrbit()
        {
            MapView.MapCamera.SetTarget(MapView.MapCamera.targets.Find((MapObject mobj) => mobj.vessel != null && mobj.vessel.GetInstanceID() == FlightGlobals.ActiveVessel.GetInstanceID()));
            if (MapView.MapIsEnabled && !wasInMapView) MapView.ExitMapView();
            wasInMapView = true;    // Not really, but this stops it from trying to force it if the user enters again.

            if (!predictionsDrawn)
                return;

            predictionOrbitRenderer.drawMode = OrbitRenderer.DrawMode.OFF;
            predictionOrbitRenderer.driver.drawOrbit = false;
            predictionOrbitRenderer.drawIcons = OrbitRenderer.DrawIcons.NONE;
            Destroy(predictionGameObject);
            predictionGameObject = null;
            predictionsDrawn = false;

            //Direction indicator is broken/not required
            //oDirection.enabled = false;

            /*Deprecated foreach (MapObject mobj in MapView.MapCamera.targets)
            {
                if (mobj.vessel == null) continue;
                if (mobj.vessel.GetInstanceID() == FlightGlobals.ActiveVessel.GetInstanceID())
                {
                    MapView.MapCamera.SetTarget(mobj);
                }
            }*/
        }

        /// <summary> Check if patched conics are available in the current save. </summary>
        /// <returns>True if patched conics are available</returns>
        public static bool IsPatchedConicsAvailable
        {
            get
            {
                // Get our level of tracking station
                float trackingstation_level = ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation);

                // Check if the tracking station knows Patched Conics
                return GameVariables.Instance.GetOrbitDisplayMode(trackingstation_level).CompareTo(
                        GameVariables.OrbitDisplayMode.PatchedConics) >= 0;
            }
        }

        // Mapview Utility
        private MapObject FindVesselBody(Vessel craft)
        {
            int cInst = craft.mainBody.GetInstanceID();
            foreach (MapObject mobj in MapView.MapCamera.targets)
            {
                if (mobj.celestialBody == null) continue;
                if (mobj.celestialBody.GetInstanceID() == cInst)
                {
                    return mobj;
                }
            }
            return null;
        }

        private static void SetupStyles()
        {
            if (buttonNeutral != null)
                return;

            buttonNeutral = new GUIStyle(HighLogic.Skin.button);
            //buttonNeutral.padding = new RectOffset(8, 8, 8, 8);
            //buttonNeutral.normal.textColor = buttonNeutral.focused.textColor = Color.white;
            //buttonNeutral.hover.textColor = buttonNeutral.active.textColor = Color.white;

            labelHasFuel = new GUIStyle(HighLogic.Skin.label);
            labelHasFuel.normal.textColor = Color.green;

            labelNoFuel = new GUIStyle(HighLogic.Skin.label);
            labelNoFuel.normal.textColor = Color.red;

            buttonHasFuel = new GUIStyle(HighLogic.Skin.button);
            //buttonHasFuel.padding = new RectOffset(8, 8, 8, 8);
            buttonHasFuel.normal.textColor = buttonHasFuel.hover.textColor = new Color(0.67f, 1, 0);// Color.green;
            buttonHasFuel.focused.textColor = buttonHasFuel.active.textColor = new Color(0.80f, 1, 0);// Color.white;

            buttonNoFuel = new GUIStyle(HighLogic.Skin.button);
            //buttonNoFuel.padding = new RectOffset(8, 8, 8, 8);
            buttonNoFuel.normal.textColor = buttonNoFuel.hover.textColor = new Color(0.89f, 0.75f, 0.06f);
            buttonNoFuel.focused.textColor = buttonNoFuel.active.textColor = Color.yellow;

            buttonNoPath = new GUIStyle(HighLogic.Skin.button);
            //buttonNoPath.padding = new RectOffset(8, 8, 8, 8);
            buttonNoPath.normal.textColor = buttonNoPath.focused.textColor = Color.black;
            buttonNoPath.hover.textColor = buttonNoPath.active.textColor = Color.black;
        }

        // Find parts that need a HCU to transfer.
        public static Dictionary<Part, string> GetHCUParts(Vessel craft)
        {
            Dictionary<Part, string> HCUParts = new Dictionary<Part, string>();
            foreach (Part vpart in craft.Parts)
            {
                foreach (PartResource vres in vpart.Resources)
                {
                    if (highEnergyResources.ContainsKey(vres.resourceName) && vres.amount > 0)
                    {
                        if (!HCUParts.Keys.Contains(vpart))
                            HCUParts.Add(vpart, vres.resourceName);
                    }
                }
            }
            return HCUParts;
        }

        private class TargetDetails
        {
            public string targetSOI = "";
            public float targetAlt = 0;
            public readonly Vessel targetVessel;
            public readonly string vesselName;
            public float tripCost = 0;
            private readonly HailerGUI hailerGUI;
            public bool affordable = false;
            public PathCheck pathCheck;
            public float precision = 0;
            public float returnCost = 0;
            public ESLDBeacon returnBeacon = null;
            public bool returnFuelCheck = false;
            public bool returnAffordable = false;

            public TargetDetails(Vessel target, HailerGUI hailerGUI)
            {
                this.hailerGUI = hailerGUI;
                targetVessel = target;
                vesselName = target.vesselName;
                if (hailerGUI.nearBeacon != null)
                    pathCheck = new PathCheck(hailerGUI.nearBeacon.Vessel, targetVessel, hailerGUI.nearBeacon.PathGLimit);
                else
                    pathCheck = new PathCheck(null, targetVessel, 0);
                Update();
            }

            public void Update()
            {
                targetSOI = targetVessel.mainBody.name;
                targetAlt = (float)targetVessel.altitude / 1000;
                if (hailerGUI.nearBeacon != null)
                    pathCheck = new PathCheck(hailerGUI.nearBeacon.Vessel, targetVessel, hailerGUI.nearBeacon.PathGLimit);
                else
                {
                    pathCheck = new PathCheck(null, targetVessel, 0);
                    return;
                }
                float tripdist = Vector3.Distance(hailerGUI.nearBeacon.Vessel.GetWorldPos3D(), targetVessel.GetWorldPos3D());
                tripCost = hailerGUI.nearBeacon.GetTripBaseCost(tripdist / ModuleESLDLens.GetLensingGainRatio(hailerGUI.nearBeacon.Vessel.GetWorldPos3D(), targetVessel.GetWorldPos3D(), farLenses), hailerGUI.tonnage);
                tripCost = hailerGUI.nearBeacon.GetTripFinalCost(tripCost, hailerGUI.vessel, targetVessel, hailerGUI.tonnage, hailerGUI.HCUPartsList);
                //float cost = tripCost;
                affordable = true;
                for (int i = hailerGUI.nearBeacon.JumpResources.Count - 1; i >= 0; i--)
                {
                    if (!hailerGUI.nearBeacon.RequireResource(hailerGUI.nearBeacon.JumpResources[i].resID, tripCost * hailerGUI.nearBeacon.JumpResources[i].ratio, false))
                    {
                        affordable = false;
                        break;
                    }
                }
                //HailerGUI hailer = hailerGUI;
                //affordable = hailerGUI.nearBeacon.JumpResources.All(res => hailer.nearBeacon.RequireResource(res.resID, res.ratio * cost, false));
                returnCost = float.PositiveInfinity;
                returnAffordable = false;
                if (farBeaconVessels.ContainsKey(targetVessel))
                {
                    returnFuelCheck = farBeaconVessels[targetVessel].Any(pb => pb.JumpResources.All(res => res.fuelCheck));
                    precision = float.MaxValue;
                    for (int i = farBeaconVessels[targetVessel].Count - 1; i >= 0; i--)
                    {
                        precision = Math.Min(farBeaconVessels[targetVessel][i].GetTripSpread(tripdist), precision);
                        float possibleReturnCost = farBeaconVessels[targetVessel][i].GetTripBaseCost(tripdist / ModuleESLDLens.GetLensingGainRatio(targetVessel.GetWorldPos3D(), hailerGUI.nearBeacon.Vessel.GetWorldPos3D(), farLenses), hailerGUI.tonnage);
                        bool beaconCanAfford = false;
                        if (returnFuelCheck)
                        {
                            beaconCanAfford = farBeaconVessels[targetVessel][i].JumpResources.All(res => res.fuelCheck && res.ratio * possibleReturnCost <= res.fuelOnBoard);
                            if (beaconCanAfford)
                                returnAffordable = true;
                        }
                        if (possibleReturnCost < returnCost && (!returnFuelCheck || !returnAffordable || beaconCanAfford))
                        {
                            returnCost = possibleReturnCost;
                            returnBeacon = farBeaconVessels[targetVessel][i];
                        }
                    }
                }
                else
                {
                    returnFuelCheck = false;
                    precision = hailerGUI.nearBeacon.GetTripSpread(tripdist);
                }
            }
        }

        public struct PathCheck
        {
            public bool clear;
            public string blockReason;
            public CelestialBody blockingBody;
            public PathCheck(Vessel pointA, Vessel pointB, float gLimit)
            {
                blockingBody = null;
                if (pointA == null || pointB == null)
                {
                    clear = false;
                    blockReason = "Null";
                    return;
                }
                clear = true;
                blockReason = "Ok!";
                // Cribbed with love from RemoteTech.  I have no head for vectors.
                Vector3d opos = pointA.GetWorldPos3D();
                Vector3d dpos = pointB.GetWorldPos3D();
                int numCelestialBodies = FlightGlobals.Bodies.Count;
                for (int i = 0; i < numCelestialBodies; i++)
                {
                    CelestialBody rock = FlightGlobals.Bodies[i];
                    Vector3d bodyFromOrigin = rock.position - opos;
                    Vector3d destFromOrigin = dpos - opos;
                    if (Vector3d.Dot(bodyFromOrigin, destFromOrigin) <= 0) continue;
                    Vector3d destFromOriginNorm = destFromOrigin.normalized;
                    if (Vector3d.Dot(bodyFromOrigin, destFromOriginNorm) >= destFromOrigin.magnitude) continue;
                    Vector3d lateralOffset = bodyFromOrigin - Vector3d.Dot(bodyFromOrigin, destFromOriginNorm) * destFromOriginNorm;
                    double limbo = Math.Sqrt((6.673E-11 * rock.Mass) / gLimit) - rock.Radius; // How low can we go?
                    string limbotype = "Gravity";
                    if (limbo < rock.Radius + rock.Radius * 0.25)
                    {
                        limbo = rock.Radius + rock.Radius * .025;
                        limbotype = "Proximity";
                    }
                    if (lateralOffset.magnitude < limbo)
                    {
                        blockingBody = rock;
                        blockReason = limbotype;
                        clear = false;
                        return;
                    }
                }
                if (FlightGlobals.getGeeForceAtPosition(pointB.GetWorldPos3D()).magnitude > gLimit)
                {
                    blockingBody = pointB.mainBody;
                    blockReason = "Gravity";
                    clear = false;
                }
            }
        }
    }
}
