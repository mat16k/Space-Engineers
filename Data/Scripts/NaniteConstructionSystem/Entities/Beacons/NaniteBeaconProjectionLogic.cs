using Sandbox.Common.ObjectBuilders;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using Sandbox.ModAPI;
using VRage;

namespace NaniteConstructionSystem.Entities.Beacons
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_OxygenFarm), true, "LargeNaniteBeaconProjection", "SmallNaniteBeaconProjection")]
    public class NaniteBeaconProjectionLogic : MyGameLogicComponent
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
                Logging.Instance.WriteLine(string.Format("ADDING Projection Beacon: {0}", Entity.EntityId));
                m_beacon = new NaniteBeaconProjection(Entity as IMyTerminalBlock);
                NaniteConstructionManager.BeaconList.Add(m_beacon);
            }
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }
    }
}
