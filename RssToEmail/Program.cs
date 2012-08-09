using System;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using QDFeedParser;
using Raven.Client.Embedded;
using RssToEmail.Models;

namespace RssToEmail
{
	class Program
	{
		static void Main(string[] args)
		{
			var urls = ConfigurationManager.AppSettings["urls"].Split(new [] { ';',',' }, StringSplitOptions.RemoveEmptyEntries);
			var to = new MailAddress(ConfigurationManager.AppSettings["to"]);
			bool sendAllForNewFeeds;
			bool.TryParse(ConfigurationManager.AppSettings["SendAllForNewFeeds"] ?? "false", out sendAllForNewFeeds);

			var feeds = new HttpFeedFactory();
			var documentStore = new EmbeddableDocumentStore
							{
								ConnectionStringName = "RavenDB"
							}.Initialize();

			foreach (var url in urls)
			{
				var feed = feeds.CreateFeed(new Uri(url));
				var from = new MailAddress(ConfigurationManager.AppSettings["from"], feed.Title);

				var session = documentStore.OpenSession();
				var savedFeed = session.Query<Feed>().SingleOrDefault(x => x.Url == url) ?? 
					new Feed {
						Url = url
					};
				var isNew = !savedFeed.SentItems.Any();
				foreach (var item in feed.Items)
				{
					// Ignore previously processed items
					if (savedFeed.SentItems.Contains(item.Id))
						continue;

					// Only send an email if the url is not new or if the user configures that new urls should send all current items
					if (!isNew  || sendAllForNewFeeds)
					{
						try
						{
							var message = new MailMessage(from, to) {
								Subject = "[New Post] " + item.Title, 
								Body = item.Content + string.Format("<p style=\"font-size:12px;line-height:1.4em;margin:10px 0px 10px 0px\">View the original article: <a href=\"{0}\">{0}</a></p>", item.Link), 
								IsBodyHtml = true
							};

							var smtp = new SmtpClient();
							smtp.Send(message);
						}
						catch (Exception ex)
						{
							// Ignore this for now... we'll just try again next time the app runs
							continue;
						}
					}

					savedFeed.SentItems.Add(item.Id);
				}
				session.Store(savedFeed);
				session.SaveChanges();
			}
		}
	}
}
