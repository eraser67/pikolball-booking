using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;

namespace PickleBallBooking.Pages
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [IgnoreAntiforgeryToken]
    public class ErrorModel : PageModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        public int? StatusCode { get; set; }

        public string FriendlyMessage { get; set; } = "An error occurred while processing your request.";

        public void OnGet(int? statusCode)
        {
            StatusCode = statusCode;
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            FriendlyMessage = statusCode switch
            {
                404 => "Sorry, the page you're looking for could not be found.",
                403 => "You don't have permission to access this page.",
                401 => "Please log in to access this page.",
                _ => "Sorry, something went wrong while processing your request. Please try again."
            };
        }
    }

}
