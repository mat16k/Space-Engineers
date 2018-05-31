using Sandbox.Common.ObjectBuilders;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game;
using System.Collections.Generic;
using VRageMath;
using VRage.Utils;
using VRage;

using NaniteConstructionSystem.Extensions;

namespace NaniteConstructionSystem.Entities.Beacons
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Projector), true, "LargeNaniteAreaBeacon")]
    public class NaniteAreaBeaconLogic : MyGameLogicComponent
    {
        private NaniteBeacon m_beacon = null;
        private static FastResourceLock m_lock = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (m_lock == null)
                m_lock = new FastResourceLock();

            base.Init(objectBuilder);

            using (m_lock.AcquireExclusiveUsing())
            {
                Logging.Instance.WriteLine(string.Format("ADDING Area Beacon: {0}", Entity.EntityId));
                m_beacon = new NaniteAreaBeacon((IMyTerminalBlock)Entity);
                NaniteConstructionManager.BeaconList.Add(m_beacon);
            }
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }
    }
}
