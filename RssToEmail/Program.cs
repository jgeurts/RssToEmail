using System;
using System.Configuration;
using System.Linq;
using System.Net.Mail;
using System.ServiceModel.Syndication;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using Raven.Client.Embedded;
using Raven.Database.Server;
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
			Console.Write("Starting raven...");
//			NonAdminHttp.EnsureCanListenToWhenInNonAdminContext(43233);
			var documentStore = new EmbeddableDocumentStore
							{
								ConnectionStringName = "RavenDB",
//								UseEmbeddedHttpServer = true
							};
//			documentStore.Configuration.Port = 43233;
			documentStore.Initialize();
			Console.WriteLine("Done");
			//using (var session = documentStore.OpenSession())
			//{
			//    Console.Read();
			//}
			using (var session = documentStore.OpenSession())
			{
				foreach (var url in urls)
				{
					var savedFeed = session.Query<Feed>().SingleOrDefault(x => x.Url == url) ?? 
						new Feed {
							Url = url
						};
					var isNew = !savedFeed.SentItems.Any();
					Console.Write("Processing " + url + "...");
					try
					{
						using (var f = XmlReader.Create(url))
						{
							var feed = SyndicationFeed.Load(f);
							var from = new MailAddress(ConfigurationManager.AppSettings["from"], feed.Title.Text);

							bool? supportsContentEncoding = null;
							foreach (var item in feed.Items.Reverse())
							{
								var linkUri = item.Links.FirstOrDefault();
								var link = string.Empty;
								if (linkUri != null)
									link = linkUri.Uri.ToString().Split('?').First();

								var id = item.Id;
								if (item.Id.Contains("?key="))
								{
									id = item.Id.Split(new [] { "?key=" }, StringSplitOptions.RemoveEmptyEntries)[1];
								}

								// Ignore previously processed items
								if (savedFeed.SentItems.Any(x => 
										x.Id.Equals(id, StringComparison.OrdinalIgnoreCase) || 
										x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase) || 
										(!string.IsNullOrEmpty(x.Url) && x.Url == link)))
									continue;

								// Only send an email if the url is not new or if the user configures that new urls should send all current items
								if (!isNew  || sendAllForNewFeeds)
								{
									try
									{
										var content = string.Empty;
										if (item.Summary != null)
										{
											content = item.Summary.Text;
										}
										else 
										{
											var textContent = item.Content as TextSyndicationContent;
											if (textContent != null)
												content = textContent.Text;
										}


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

										var message = new MailMessage(from, to) {
											Subject = "[New Post] " + item.Title.Text, 
											Body = content + string.Format("<p style=\"font-size:12px;line-height:1.4em;margin:10px 0px 10px 0px\">View the original article: <a href=\"{0}\">{0}</a></p>", link), 
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

								savedFeed.SentItems.Add(new FeedItem
								{
									Id = id,
									Url = link
								});
							}
						}
						session.Store(savedFeed);
						Console.WriteLine("Done");
					}
					catch (Exception ex)
					{
						Console.WriteLine("\n" + ex);
					}
				}
				session.SaveChanges();
			}
		}
	}
}
