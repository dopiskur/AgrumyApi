using api.Dal.Interface;
using api.Models;
using Org.BouncyCastle.Asn1.Esf;
using Org.BouncyCastle.Crypto.Parameters;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Permissions;
using System.Text;

namespace api.Security
{
    public class Auth2Provider
    {

        //Default GUID "792ff584-b447-474e-8566-041de43fd9b3

        // Zasto je null reference?!!?!
        // private static readonly byte[] salt = new Guid(RepositoryFactory.GetRepository().ServerConfigGet(1).ConfigKey.ToString()).ToByteArray();

        // private static readonly byte[] salt = Guid.NewGuid().ToByteArray();




        public static bool VerifyDevice(AuthenticationHeaderValue apiId, AuthenticationHeaderValue apiKey)
        {


            // Treba napravit da se salje samo apiid i 
            Device device = RepoFactory.GetRepo().DeviceGet(0,null,apiId.ToString(),null); //popravu tenant
            if (device == null)
            {
                // upisi u log da device ne postoji
                return false;
            }

            if (apiKey.ToString() == device.ApiKey)
            {
                return true;
            }

            return false;
        }

    }
}
