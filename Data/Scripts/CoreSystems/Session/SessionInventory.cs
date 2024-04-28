using System.Collections.Generic;
using CoreSystems.Platform;
using Sandbox.ModAPI;

namespace CoreSystems
{
    public partial class Session
    {
        internal void StartAmmoTask()
        {
            InventoryUpdate = true;
            if (ITask.valid && ITask.Exceptions != null)
                TaskHasErrors(ref ITask, "ITask");
            ITask = MyAPIGateway.Parallel.StartBackground(ProcessAmmoMoves, ProcessConsumableCallback);
        }

        internal void ProcessAmmoMoves() // In Thread
        {
            foreach (var pair in PartToPullConsumable)
            {
                var part = pair.Key;
                using (part.BaseComp.TopEntity.Pin())
                using (part.BaseComp.CoreEntity.Pin())
                {

                    if (part.BaseComp.CoreEntity.MarkedForClose || part.BaseComp.Ai == null || part.BaseComp.Ai.MarkedForClose || part.BaseComp.TopEntity.MarkedForClose || !part.BaseComp.InventoryInited || part.BaseComp.Platform.State != CorePlatform.PlatformState.Ready)
                    {
                        InvPullClean.Add(part);
                        continue;
                    }
                    var topUp = pair.Value == 0;
                    var pullAmount = pair.Value == 0 ? 1 : pair.Value;
                    var cube = part.BaseComp.Cube;
                    var pulled = cube.CubeGrid.ConveyorSystem.PullItem(part.ActiveAmmoDef.AmmoDefinitionId, pullAmount, cube, part.BaseComp.CoreInventory, false);
                    if (pulled == pair.Value)//pulled all requested
                    {
                        if (topUp)
                        {
                            part.NextInventoryTick = Tick + 59;
                            //Log.Line($"ProcessAmmoMoves pulled topup");

                        }
                        else
                        {
                            part.InventoryChecks = 0;
                            part.NextInventoryTick = int.MaxValue;
                            var removed = part.BaseComp.Ai.Construct.RootAi.Construct.OutOfAmmoWeapons.Remove(part);
                            //Log.Line($"ProcessAmmoMoves pulled all {pulled}, was in outofammoweapons? {removed}");
                        }
                        part.InventoryChecks = 0;
                    }
                    else//partial or zero pull
                    {
                        part.InventoryChecks++;
                        if (part.InventoryChecks >= 4) //Exceeded max attempts, sit on event based out of ammo weapons list
                        {
                            part.NextInventoryTick = int.MaxValue;
                            var added = part.BaseComp.Ai.Construct.RootAi.Construct.OutOfAmmoWeapons.Add(part);
                            //Log.Line($"ProcessAmmoMoves pulled zero or partial {pulled}/{pair.Value}  Giving up. Added to outofammoweapons {added}");
                        }
                        else//Queue next attempt time
                        {
                            part.NextInventoryTick = Tick + (19 * part.InventoryChecks);
                            //Log.Line($"ProcessAmmoMoves pulled zero or partial {pulled}/{pair.Value}  next: {part.NextInventoryTick}");
                        }
                    }
                }
                InvPullClean.Add(part);
            }
        }

        internal void ProcessConsumableCallback()
        {
            for (int i = 0; i < InvPullClean.Count; i++) 
            {
                var weapon = InvPullClean[i];
                PartToPullConsumable.Remove(weapon);
            }
            InvPullClean.Clear();           
            InventoryUpdate = false;
        }
    }
}
