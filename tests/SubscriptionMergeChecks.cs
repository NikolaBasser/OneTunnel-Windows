using OneTunnel.Core;

internal static class SubscriptionMergeChecks
{
    internal static void Run(Action<bool,string> assert)
    {
        Profile P(string name,int port,string group="subscription")=>ProfileParser.Parse(name,$"socks5://127.0.0.1:{port}") with{Group=group};
        var old=P("🇩🇪 Node",1080) with {Transport=new TransportOptions{FragmentLength="150-250"}};var retired=P("Retired",1081);var rename=P("Old title",1082);var unrelated=P("🇩🇪 Node",1083,"personal");
        var native=new Profile(Guid.NewGuid(),"Native",Protocol.WireGuard,"kept verbatim"){Group="subscription"};
        var incoming=new[]{P("🇩🇪 Node",2080),P("New title",1082),P("New node",2082)};
        var merged=SubscriptionMerge.Refresh([old,retired,rename,unrelated,native],incoming,"subscription");
        assert(merged.Profiles.Single(p=>p.Name==old.Name&&p.Group=="subscription").Id==old.Id&&merged.Profiles.Single(p=>p.Id==old.Id).Source==incoming[0].Source,"subscription refresh replaces rotated endpoint and preserves profile ID");
        assert(merged.Profiles.Single(p=>p.Name=="New title").Id==rename.Id,"subscription refresh preserves identity across renamed node");
        assert(merged.Profiles.Single(p=>p.Id==old.Id).Transport==old.Transport,"subscription refresh preserves transport preferences independently of credentials");
        assert(!merged.Profiles.Any(p=>p.Id==retired.Id)&&merged.Removed==1,"subscription refresh removes retired provider node");
        assert(merged.Profiles.Contains(unrelated)&&merged.Profiles.Contains(native),"subscription refresh preserves personal groups and native configurations");
        assert(merged.Added==1&&merged.Updated==2&&merged.Profiles.Count==5,"subscription refresh counts additions and updates without duplicates");
        var again=SubscriptionMerge.Refresh(merged.Profiles,incoming,"subscription");
        assert(again.Added==0&&again.Updated==0&&again.Removed==0&&again.Profiles.SequenceEqual(merged.Profiles),"subscription reimport is idempotent");
        var empty=SubscriptionMerge.Refresh(merged.Profiles,[],"subscription");
        assert(empty.Profiles.SequenceEqual(merged.Profiles),"empty import cannot erase existing profiles");
    }
}
