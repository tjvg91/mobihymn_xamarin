using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MobiHymn4.Models
{
	public interface IFirebaseHelper
	{
        Task LoginWithEmailPassword(string email, string password);
    }
}

