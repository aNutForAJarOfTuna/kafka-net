﻿/*
 * Created by SharpDevelop.
 * User: peng.zang
 * Date: 11/10/2014
 * Time: 2:44 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

using KafkaNet.Model;
using KafkaNet.Protocol;

using ZooKeeperNet;

namespace KafkaNet
{
	/// <summary>
	/// Description of HLConsumer.
	/// </summary>
	public class HLConsumer
	{
		KafkaOptions _options ;
		BrokerRouter _router ;
		Consumer _consumer;
		ZooKeeper _zookeeper;
		string _topic;
		IWatcher _watcher;
		
		public HLConsumer(string topic, List<string> brokerList, string zookps, TimeSpan? timeout = null, IWatcher watcher = null)
		{
			_options = new KafkaOptions();
			_options.KafkaServerUri = brokerList.ConvertAll<Uri>(x => new Uri(x));
			
			_router = new BrokerRouter(_options);
			_consumer = new KafkaNet.Consumer(new ConsumerOptions(topic, _router));
			
			_topic = topic;
			
			_zookeeper = new ZooKeeper(zookps, timeout.HasValue ? timeout.Value : new TimeSpan(7,0,0) , watcher);
			_watcher = watcher;
		}
		
		public IEnumerable<Message> consume(string groupID){
			var path = new StringBuilder( "/consumers" ); //+ groupID + "/offsets/" + this._topic ;
			var p = "/consumers/"+groupID+"/offsets/"+this._topic;
			try {
				if(_zookeeper.Exists(p , _watcher) ==null){
					if(_zookeeper.Exists(path.ToString(), _watcher) ==null){
						_zookeeper.Create(path.ToString(), new byte[0], Ids.CREATOR_ALL_ACL, CreateMode.Persistent);
						
					} else if(_zookeeper.Exists(path.Append("/").Append(groupID).ToString(), _watcher) == null){
						_zookeeper.Create(path.ToString(), new byte[0], Ids.CREATOR_ALL_ACL, CreateMode.Persistent);
						
					} else if (_zookeeper.Exists(path.Append("/offsets").ToString(), _watcher) == null){
						_zookeeper.Create(path.ToString(), new byte[0], Ids.CREATOR_ALL_ACL, CreateMode.Persistent);
						
					} else if (_zookeeper.Exists(path.Append("/").Append(this._topic).ToString(), _watcher) == null){
						_zookeeper.Create(path.ToString(), new byte[0], Ids.CREATOR_ALL_ACL, CreateMode.Persistent);
					}

					var common = new MetadataQueries(_router);
					var offsets = common.GetTopicOffsetAsync(groupID).Result;
					_consumer.SetOffsetPosition(offsets.Select(x => new OffsetPosition(x.PartitionId, x.Offsets.Min())).ToArray());
					offsets.ForEach(off => {
					                	_zookeeper.Create(p + "/" + off.PartitionId.ToString(),
					                                  System.Text.Encoding.UTF8.GetBytes(off.Offsets.Min().ToString()),
					                                  Ids.OPEN_ACL_UNSAFE,
					                                  CreateMode.PersistentSequential);
					                });
				}
				else {
					var children = _zookeeper.GetChildren( p, _watcher);	//TODO: add watcher and stat support.
					var offsets = new List<OffsetPosition>();
					children.ToList().ForEach(x => {
					                          	int partition;
					                          	if(int.TryParse(x, out partition) ){
					                          		var data = _zookeeper.GetData(p + "/" + partition, _watcher, null);
					                          		if(data != null && data.Length >0){
					                          			long offset = 0;
					                          			if(long.TryParse(System.Text.Encoding.Default.GetString(data), out offset)){
//					                          				_consumer.SetOffsetPosition(new OffsetPosition(partition, offset));
					                          				offsets.Add(new OffsetPosition(partition, offset));
					                          			}
					                          		}
					                          	}
					                          });
					_consumer.SetOffsetPosition(offsets.ToArray());
				}
			} catch (Exception e) {
				//TODO: Log the error, or handle it? 
			}
			
			return _consumer.Consume();
		}
	}
}