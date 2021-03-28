using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ESLDCore
{
    public class ESLDHailer : PartModule
    {
        //public Transform oOrigin = null;
        //public LineRenderer oDirection = null;
        Logger log = new Logger("ESLDCore:ESLDHailer: ");

        private HailerGUI hailerGui = null;
        public HailerGUI AttachedGui
        {
            get => hailerGui;
            set
            {
                hailerGui = value;
                if (hailerGui == null)
                {
                    Events["HailerGUIClose"].active = false;
                    Events["HailerGUIOpen"].active = hailerActive;
                }
                else
                {
                    Events["HailerGUIOpen"].active = false;
                    Events["HailerGUIClose"].active = hailerActive;
                }
            }
        }
        private ESLDBeacon nearBeacon = null;

        [KSPField(guiActive = false, isPersistant = true)]
        public bool hailerActive = false;

        [KSPField(guiName = "Beacon", guiActive = false)]
        public string hasNearBeacon;

        [KSPField(guiName = "Beacon Distance", guiActive = false, guiUnits = "m", guiFormat = "F1")]
        public double nearBeaconDistance;

        [KSPField(guiName = "Drift", guiActive = false, guiUnits = "m/s", guiFormat = "F2")]
        public double nearBeaconRelVel;
        
        [KSPEvent(name = "HailerActivate", active = true, guiActive = true, guiName = "Initialize Hailer")]
        public void HailerActivate()
        {
            Events["HailerActivate"].active = false;
            Events["HailerDeactivate"].active = true;
            Events["HailerGUIOpen"].active = hailerGui == null;
            Events["HailerGUIClose"].active = hailerGui != null;
            Fields["hasNearBeacon"].guiActive = true;
            Fields["nearBeaconDistance"].guiActive = true;
            Fields["nearBeaconRelVel"].guiActive = true;
            hailerActive = true;
        }
        [KSPEvent(name = "HailerGUIOpen", active = false, guiActive = true, guiName = "Beacon Interface")]
        public void HailerGUIOpen()
        {
            Events["HailerGUIOpen"].active = false;
            Events["HailerGUIClose"].active = true;
            hailerGui = HailerGUI.ActivateGUI(vessel);
        }
        [KSPEvent(name = "HailerGUIClose", active = false, guiActive = true, guiName = "Close Interface")]
        public void HailerGUIClose()
        {
            Events["HailerGUIClose"].active = false;
            Events["HailerGUIOpen"].active = true;
            HailerGUI.CloseGUI(vessel);
        }
        [KSPEvent(name = "HailerDeactivate", active = false, guiActive = true, guiName = "Shut Down Hailer")]
        public void HailerDeactivate()
        {
            hailerActive = false;
            if (vessel.FindPartModulesImplementing<ESLDHailer>().Any(hailer=>hailer.hailerActive) == false)
                HailerGUI.CloseGUI(vessel);
            Events["HailerDeactivate"].active = false;
            Events["HailerActivate"].active = true;
            Events["HailerGUIOpen"].active = false;
            Events["HailerGUIClose"].active = false;
            Fields["hasNearBeacon"].guiActive = false;
            Fields["nearBeaconDistance"].guiActive = false;
            Fields["nearBeaconRelVel"].guiActive = false;
        }

        public override void OnUpdate()
        {
            if (!hailerActive)
                return;
            if (hailerGui != null)
            {
                nearBeacon = hailerGui.nearBeacon;
                return;
            }

            nearBeacon = LimitedBeaconSearch();
            bool present = nearBeacon != null;
            Fields["nearBeaconDistance"].guiActive = present;
            Fields["nearBeaconRelVel"].guiActive = present;
            hasNearBeacon = present ? "Present" : "Not Present";

            if (!present)
                return;

            nearBeaconDistance = Vector3.Distance(vessel.GetWorldPos3D(), nearBeacon.Vessel.GetWorldPos3D());
            nearBeaconRelVel = Vector3.Magnitude(vessel.obt_velocity - nearBeacon.Vessel.obt_velocity);
        }

        private ESLDBeacon LimitedBeaconSearch()
        {
            ESLDBeacon nearBeacon = null;
            float distance = float.MaxValue;
            for (int i = FlightGlobals.VesselsLoaded.Count - 1; i >= 0; i--)
            {
                float vesselDistance = Vector3.Distance(vessel.GetWorldPos3D(), FlightGlobals.VesselsLoaded[i].GetWorldPos3D());
                if (vesselDistance >= distance)
                    continue;

                List<ESLDBeacon> beaconsOnVessel = FlightGlobals.VesselsLoaded[i].FindPartModulesImplementing<ESLDBeacon>();
                for (int j = beaconsOnVessel.Count - 1; j >= 0; j--)
                {
                    if (beaconsOnVessel[j].activated && beaconsOnVessel[j].moduleIsEnabled && (FlightGlobals.VesselsLoaded[i] != vessel || beaconsOnVessel[j].canJumpSelf))
                    {
                        nearBeacon = beaconsOnVessel[j];
                        distance = vesselDistance;
                        break;
                    }
                }
            }
            return nearBeacon;
        }

        public override void OnStart(StartState state)
        {
            if (hailerActive)
                HailerActivate();
        }
    }
}
