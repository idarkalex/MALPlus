using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MALPlus.Models.Enums;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Comm.MagicalRawQueries
{
    public static class MalHelpfulReviewQuery
    {
        public static async Task<bool> MarkReviewHelpful(string id)
        {
            try
            {
                var client = await ResourceLocator.MalHttpContextProvider.GetHttpContextAsync();

                var charCont = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("id", id.ToString()),
                    new KeyValuePair<string, string>("val", "1"),
                    new KeyValuePair<string, string>("csrf_token", client.Token)
                };
                var content = new FormUrlEncodedContent(charCont);
                var resp = await client.PostAsync($"https://myanimelist.net/includes/ajax.inc.php?t=72", content);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
