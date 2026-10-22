namespace OneTunnel.Core;

public sealed record SubscriptionMergeResult(List<Profile> Profiles, int Added, int Updated, int Removed);

public static class SubscriptionMerge
{
    // An explicit URL reimport refreshes the selected subscription group.
    // Native/custom protocol groups outside that scope are never touched.
    public static SubscriptionMergeResult Refresh(IReadOnlyList<Profile> existing, IReadOnlyList<Profile> incoming, string group)
    {
        var protocols=incoming.Select(p=>p.Protocol).ToHashSet();
        var target=existing.Where(p=>p.Group==group&&protocols.Contains(p.Protocol)).ToList();
        var identities=target.ToDictionary(p=>p.Id,Subscriptions.Identity);
        var used=new HashSet<Guid>();
        var replacement=new List<Profile>();int added=0,updated=0;
        foreach(var fresh in incoming)
        {
            // Identity handles a renamed node; a unique protocol/name handles
            // provider changes to endpoint, credentials, SNI and REALITY IDs.
            string identity=Subscriptions.Identity(fresh);
            var previous=target.FirstOrDefault(p=>!used.Contains(p.Id)&&p.Protocol==fresh.Protocol&&identities[p.Id]==identity);
            if(previous==null)
            {
                var byName=target.Where(p=>!used.Contains(p.Id)&&p.Protocol==fresh.Protocol&&p.Name==fresh.Name).ToList();
                if(byName.Count==1)previous=byName[0];
            }
            if(previous==null){replacement.Add(fresh with{Group=group});added++;}
            else
            {
                used.Add(previous.Id);
                if(previous.Source!=fresh.Source||previous.Name!=fresh.Name)updated++;
                replacement.Add(fresh with{Id=previous.Id,Group=group,Transport=previous.Transport});
            }
        }
        var targetIds=target.Select(p=>p.Id).ToHashSet();
        var result=new List<Profile>();bool inserted=false;
        foreach(var p in existing)
        {
            if(!targetIds.Contains(p.Id)){result.Add(p);continue;}
            if(!inserted){result.AddRange(replacement);inserted=true;}
        }
        if(!inserted)result.AddRange(replacement);
        return new(result,added,updated,target.Count-used.Count);
    }
}
