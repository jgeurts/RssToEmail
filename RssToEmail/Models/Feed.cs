using System;
using System.Collections.Generic;

namespace RssToEmail.Models
{
	public class Feed
	{
		public string Id { get; set; }
		public string Url { get; set; }
		public IList<FeedItem> SentItems { get; set; }

		public Feed()
		{
			SentItems = new List<FeedItem>();
		}
	}
}
