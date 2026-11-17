using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Members;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Tests.Features.Members;

public sealed class MemberAccessTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static UserDetails User(UserKind kind, Guid? tenant) =>
        new(Guid.NewGuid(), "u", "U", kind, tenant, true, [], DateTimeOffset.UtcNow);

    [Fact]
    public void A_tenant_admin_manages_the_people_of_their_own_tenant_only()
    {
        var admin = User(UserKind.TenantAdmin, TenantA);

        Assert.True(MemberAccess.MayManage(admin, User(UserKind.Member, TenantA)));
        Assert.True(MemberAccess.MayManage(admin, User(UserKind.TenantAdmin, TenantA)));
        Assert.False(MemberAccess.MayManage(admin, User(UserKind.Member, TenantB)));
        Assert.False(MemberAccess.MayManage(admin, User(UserKind.PlatformAdmin, null)));
    }

    [Fact]
    public void The_platform_admin_manages_tenant_admins_but_never_members()
    {
        var platform = User(UserKind.PlatformAdmin, null);

        Assert.True(MemberAccess.MayManage(platform, User(UserKind.TenantAdmin, TenantA)));
        Assert.False(MemberAccess.MayManage(platform, User(UserKind.Member, TenantA)));
        Assert.False(MemberAccess.MayManage(platform, User(UserKind.PlatformAdmin, null)));
    }

    [Fact]
    public void A_member_manages_nobody()
    {
        var member = User(UserKind.Member, TenantA);

        Assert.False(MemberAccess.MayManage(member, User(UserKind.Member, TenantA)));
    }

    [Fact]
    public void Permissions_follow_from_the_kind_and_only_members_switches_count()
    {
        Assert.Equal(new[] { Permissions.Platform.Tenants }, UserKinds.For(UserKind.PlatformAdmin, [Permissions.Broadcasts.Send]).ToArray());
        Assert.DoesNotContain(Permissions.Platform.Tenants, UserKinds.For(UserKind.TenantAdmin, []));
        Assert.Contains(Permissions.Members.Manage, UserKinds.For(UserKind.TenantAdmin, []));

        var member = UserKinds.For(UserKind.Member, [Permissions.Broadcasts.Send, Permissions.Members.Manage, "made.up"]);
        Assert.Contains(Permissions.Broadcasts.Send, member);
        Assert.DoesNotContain(Permissions.Members.Manage, member);
        Assert.DoesNotContain("made.up", member);
        Assert.Contains(Permissions.Messages.Send, member);
    }
}
