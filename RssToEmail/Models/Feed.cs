using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RssToEmail.Models
{
	public class Feed
	{
		public string Id { get; set; }
		public string Url { get; set; }
		public IList<string> SentItems { get; set; }

		public Feed()
		{
			SentItems = new List<string>();
		}
	}
}
