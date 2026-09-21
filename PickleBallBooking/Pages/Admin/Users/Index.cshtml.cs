using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PickleBallBooking.Services;

namespace PickleBallBooking.Pages.Admin.Users;

/// <summary>
/// Phase 23: lists the members of the CURRENT (hostname-resolved) organization.
///
/// Read-only by design for this phase: it uses the existing OrganizationMember
/// relationship and does not introduce a staff invitation/role-management system.
/// The organization is always the resolved tenant - never a client-supplied id.
/// </summary>
[Authorize(Policy = TenantAdminAuthorization.TenantAdminPolicy)]
public class IndexModel : PageModel
{
    private readonly IOrganizationService _organizationService;

    public IndexModel(IOrganizationService organizationService)
    {
        _organizationService = organizationService;
    }

    public List<OrganizationMemberView> Members { get; set; } = new();

    public async Task OnGetAsync()
    {
        Members = await _organizationService.GetCurrentMembersAsync();
    }
}
