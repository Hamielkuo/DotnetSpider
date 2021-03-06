﻿using DotnetSpider.Core;
using DotnetSpider.Core.Infrastructure;
using MySql.Data.MySqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace DotnetSpider.Extension
{
	public abstract class CustomSpider : IRunable, INamed, IIdentity
	{
		private bool _exited;

		private Task _statusReporter;

		public string Name { get; set; }

		public string ConnectString { get; set; }

		public string Identity { get; set; }

		protected CustomSpider(string name)
		{
			Name = name;

			if (string.IsNullOrEmpty(Name) || Name.Length > 120)
			{
				throw new ArgumentException("Length of name should between 1 and 120.");
			}
		}

		protected abstract void ImplementAction(params string[] arguments);

		public void Run(params string[] arguments)
		{
			if (string.IsNullOrEmpty(ConnectString))
			{
				ConnectString = Core.Infrastructure.Configuration.GetValue(Core.Infrastructure.Configuration.LogAndStatusConnectString);
			}

			if (!string.IsNullOrEmpty(ConnectString))
			{
				InsertRunningState();

				using (IDbConnection conn = new MySqlConnection(ConnectString))
				{
					conn.Open();
					var command = conn.CreateCommand();
					command.CommandType = CommandType.Text;

					command.CommandText = $"insert ignore into dotnetspider.status (`identity`, `status`,`thread`, `left`, `success`, `error`, `total`, `avgdownloadspeed`, `avgprocessorspeed`, `avgpipelinespeed`, `logged`) values('{Identity}', 'Init',-1, -1, -1, -1, -1, -1, -1, -1, '{DateTime.Now}');";
					command.ExecuteNonQuery();

					var message = $"开始任务: {Name}";
					command.CommandText = $"insert into dotnetspider.log (identity, node, logged, level, message) values ('{Identity}', '{NodeId.Id}', '{DateTime.Now}', 'Info', '{message}');";
					command.ExecuteNonQuery();
				}

				_statusReporter = Task.Factory.StartNew(() =>
				{
					using (IDbConnection conn = new MySqlConnection(ConnectString))
					{
						conn.Open();
						var command = conn.CreateCommand();
						command.CommandType = CommandType.Text;

						while (!_exited)
						{
							command.CommandText = $"update dotnetspider.status set `logged`='{DateTime.Now}' WHERE identity='{Identity}';";
							command.ExecuteNonQuery();
							Thread.Sleep(5000);
						}
					}
				});
			}

			try
			{
				ImplementAction(arguments);

				_exited = true;
				_statusReporter.Wait();

				if (!string.IsNullOrEmpty(ConnectString))
				{
					using (IDbConnection conn = new MySqlConnection(ConnectString))
					{
						conn.Open();
						var command = conn.CreateCommand();
						command.CommandType = CommandType.Text;

						var message = $"结束任务: {Name}";
						command.CommandText = $"insert into dotnetspider.log (identity, node, logged, level, message) values ('{Identity}','{NodeId.Id}', '{DateTime.Now}', 'Info', '{message}');";
						command.ExecuteNonQuery();

						command.CommandText = $"update dotnetspider.status set `status`='Finished',`logged`='{DateTime.Now}' WHERE identity='{Identity}';";
						command.ExecuteNonQuery();
					}
				}
			}
			catch (Exception e)
			{
				if (!string.IsNullOrEmpty(ConnectString))
				{
					using (IDbConnection conn = new MySqlConnection(ConnectString))
					{
						conn.Open();
						var command = conn.CreateCommand();
						command.CommandType = CommandType.Text;

						var message = $"退出任务: {Name}";
						command.CommandText = $"insert into dotnetspider.log (identity, node, logged, level, message, callsite, exception) values ('{Identity}','{NodeId.Id}','{DateTime.Now}', 'Info', '{message}','{e}','{e.Message}');";
						command.ExecuteNonQuery();

						command.CommandText = $"update dotnetspider.status set `status`='Exited' `logged`='{DateTime.Now}' WHERE identity='{Identity}';";
						command.ExecuteNonQuery();
					}
				}
			}
			finally
			{
				RemoveRunningState();
			}
		}

		public Task RunAsync(params string[] arguments)
		{
			return Task.Factory.StartNew(() =>
			{
				Run(arguments);
			});
		}

		private void InsertRunningState()
		{
			using (IDbConnection conn = new MySqlConnection(ConnectString))
			{
				conn.Open();
				var command = conn.CreateCommand();
				command.CommandType = CommandType.Text;

				command.CommandText = "CREATE SCHEMA IF NOT EXISTS `dotnetspider` DEFAULT CHARACTER SET utf8mb4;";
				command.ExecuteNonQuery();

				command.CommandText = "CREATE TABLE IF NOT EXISTS `dotnetspider`.`task_running` (`id` bigint(20) NOT NULL AUTO_INCREMENT, `taskId` varchar(120) NOT NULL, `cdate` timestamp NOT NULL, PRIMARY KEY (id), UNIQUE KEY `taskId_unique` (`taskId`)) ENGINE=InnoDB AUTO_INCREMENT=1  DEFAULT CHARSET=utf8";
				command.ExecuteNonQuery();

				command.CommandText = $"INSERT IGNORE INTO `dotnetspider`.`task_running` (`taskId`,`cdate`) values ('{Identity}','{DateTime.Now}');";
				command.ExecuteNonQuery();
			}
		}

		private void RemoveRunningState()
		{
			using (IDbConnection conn = new MySqlConnection(ConnectString))
			{
				conn.Open();
				var command = conn.CreateCommand();
				command.CommandType = CommandType.Text;

				command.CommandText = $"DELETE FROM `dotnetspider`.`task_running` WHERE `taskId`='{Identity}';";
				command.ExecuteNonQuery();
			}
		}
	}
}
