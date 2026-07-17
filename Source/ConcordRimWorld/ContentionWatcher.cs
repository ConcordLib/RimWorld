using System;
using System.Collections.Generic;
using System.Reflection;

namespace Concord.RimWorld;

public sealed class ContentionWatcher
{
    private readonly Func<IReadOnlyCollection<MethodBase>> rawPinnedTargets;
    private readonly Func<MethodBase, IReadOnlyList<string>> foreignOwners;
    private readonly Action<string> warn;
    private readonly Action<string> dialogOnce;
    private readonly HashSet<MethodBase> seenDialogTargets = new HashSet<MethodBase>();

    public ContentionWatcher(
        Func<IReadOnlyCollection<MethodBase>> rawPinnedTargets,
        Func<MethodBase, IReadOnlyList<string>> foreignOwners,
        Action<string> warn,
        Action<string> dialogOnce)
    {
        this.rawPinnedTargets = rawPinnedTargets;
        this.foreignOwners = foreignOwners;
        this.warn = warn;
        this.dialogOnce = dialogOnce;
    }

    public void RunCheckpoint()
    {
        IReadOnlyCollection<MethodBase> targets = rawPinnedTargets();
        foreach (MethodBase target in targets)
        {
            IReadOnlyList<string> owners = foreignOwners(target);
            if (owners.Count == 0)
            {
                continue;
            }

            string ownerList = string.Join(", ", owners);
            string warningMessage = $"{CoexistenceLogMarkers.LateContention} {target} patched by [{ownerList}]. Concord injections on this method are not running.";
            warn(warningMessage);

            if (!seenDialogTargets.Contains(target))
            {
                seenDialogTargets.Add(target);
                dialogOnce(warningMessage);
            }
        }
    }
}
