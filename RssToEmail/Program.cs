using System;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.ServiceModel.Syndication;
using System.Web;
using System.Xml;
using System.Xml.Linq;
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

			var documentStore = new EmbeddableDocumentStore
							{
								ConnectionStringName = "RavenDB"
							}.Initialize();

			using (var session = documentStore.OpenSession())
			{
				foreach (var url in urls)
				{
					var savedFeed = session.Query<Feed>().SingleOrDefault(x => x.Url == url) ?? 
						new Feed {
							Url = url
						};
					var isNew = !savedFeed.SentItems.Any();

					using (var f = XmlReader.Create(url))
					{
						var feed = SyndicationFeed.Load(f);
						var from = new MailAddress(ConfigurationManager.AppSettings["from"], feed.Title.Text);

						bool? supportsContentEncoding = null;
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
									var content = item.Summary.Text;

									if (!supportsContentEncoding.HasValue || supportsContentEncoding.Value)
									{
										if (!supportsContentEncoding.HasValue)
											supportsContentEncoding = false;

										// If the feed has a <content:encoded> item, use that instead of what comes from <description>
										foreach (var extension in item.ElementExtensions)
										{
											var element = extension.GetObject<XElement>();
											if (element.Name.LocalName == "encoded" && element.Name.Namespace.ToString().Contains("content"))
											{
												content = element.Value;
												supportsContentEncoding = true;
												break;
											}
										}
									}

									var link = item.Links.FirstOrDefault();

									var message = new MailMessage(from, to) {
										Subject = "[New Post] " + item.Title.Text, 
										Body = content + string.Format("<p style=\"font-size:12px;line-height:1.4em;margin:10px 0px 10px 0px\">View the original article: <a href=\"{0}\">{0}</a></p>", link != null ? link.Uri.ToString() : string.Empty), 
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
					}
					session.Store(savedFeed);
				}
				session.SaveChanges();
			}
		}
	}
}
