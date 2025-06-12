using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Contracts.Requests.Auth
{
    public class ConfirmEmailRequest
    {
        [Required]
        public string Token { get; set; }
    }
}
