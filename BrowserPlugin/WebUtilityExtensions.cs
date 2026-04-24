using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BrowserPlugin
{
    internal static class WebUtilityExtensions
    {
        public static string Hash(this string value)
        {
            using MD5 md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
            StringBuilder sb = new StringBuilder();
            foreach (var item in hash)
            {
                sb.Append(item.ToString("x2"));
            }
            return sb.ToString();
        }
        public static string HtmlEncode(this string value)
        {
            return System.Net.WebUtility.HtmlEncode(value);
        }
        public static string HtmlDecode(this string value)
        {
            return System.Net.WebUtility.HtmlDecode(value);
        }
        public static string UrlEncode(this string value)
        {
            return System.Net.WebUtility.UrlEncode(value);
        }
        public static string UrlDecode(this string value)
        {
            return System.Net.WebUtility.UrlDecode(value);
        }
    }
}
