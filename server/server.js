"use strict";

if( !process.env.NODE_ENV )
  process.env.NODE_ENV = 'development';

var http = require('http');
var Settings = require('settings');
var config = new Settings(require('./config'));

var watcher = require('./watcher')(config);
var s3sync = require('./s3sync')(config);

watcher.on('file.existing', s3sync.upload);
watcher.on('file.new', s3sync.upload);

var server = http.createServer(function(req, res){
  res.end('ok');
});
server.listen(config.port, function(){
  console.info('image sync running %d - %s', config.port, config.ENV);
});