"use strict";

const path = require('path');
const async = require('async');
const mime = require('mime');
const fs = require('fs');
var mongoose = require('mongoose');
var knox = require('knox');
var buckets = new Map();

/**
 * S3 object to store config and upload state
 * @param config
 * @returns {s3}
 */
var s3 = function(config){
  s3.config = config;

  mongoose.connect(config.mongo_url);
  s3.filestatus = mongoose.model('FileStatus', mongoose.Schema({
    file: String,
    path : String,
    bucket : String,
    uploaded : Boolean,
    reason : Object
  }));

  return s3;
};
/**
 * Determines the bucket from the full file path
 * @param file
 * @returns {*}
 */
s3.getBucketFromPath = function(file){

  var relpath = file.replace(s3.config.watch + path.sep, '');
  return relpath.split(path.sep)[0];
};

/**
 * Creates a queue for a bucket
 * @param bucket
 * @returns {*|queue}
 */
function queue(bucket){

  var client = knox.createClient({
    key: s3.config.aws_key,
    secret: s3.config.aws_secret,
    bucket: bucket
  });

  function upload(file, done) {

    console.info(`uploading ${file.path}`);

    fs.stat(file.path, (err, stat) => {

      if (err) return done(err);
      var contentType = mime.lookup(file.path);
      var charset = mime.charsets.lookup(contentType);
      if (charset)
        contentType += '; charset=' + charset;

      var headers = {
        'Content-Length': stat.size,
        'Content-Type': contentType
      };
      var stream = fs.createReadStream(file.path);
      var req = client.putStream(stream, file.rel, headers, done);
      if( s3.config.timeout > 0)
        req.setTimeout(s3.config.timeout, () => {
          req.abort();
        });
    });
  }

  var q = async.queue(upload, s3.config.bucket_concurrency);
  q.drain = function() {
    console.info(`${bucket} queue is empty`);
  };
  return q;
}
/**
 * Connects a bucket to a queue
 * @param bucket
 * @returns {*}
 */
s3.connect = function(bucket){

  var conn = buckets.get(bucket);
  if( conn ) return conn;
  var client = queue(bucket);
  buckets.set(bucket, client);

  return client;
};
/**
 * Upload a file to a bucket
 * @param file
 * @param done
 */
s3.upload = function(file, done){

  if( !done ) done = function(){};
  var bucket = s3.getBucketFromPath(file);
  var relpath = file.replace(s3.config.watch + path.sep + bucket + path.sep, '');
  var queue = s3.connect(bucket);

  isUploaded(file, function(err, uploaded){

    if( uploaded ) return done();

    console.info(`queueing ${file}`);

    queue.push({path:file,rel:relpath}, (err, res) => {

      var status = {
        file: file,
        path : relpath,
        bucket : bucket,
        uploaded : res && res.statusCode === 200,
        reason : err
      };

      if( !status.uploaded ){
        console.error(`unable to upload file ${file}`, err || res && res.statusCode, res && res.body);
        // try this again in 10 seconds
        setTimeout(() => {s3.upload(file, done);}, 10000);
      }else{
        console.info(`uploaded ${file}`);
      }
      s3.filestatus.findOneAndUpdate({file: file}, status, {upsert: true}, done);
    });
  });
};

function isUploaded(file, done){
  s3.filestatus.findOne({file:file}, function(err, res){
    var uploaded = false;
    if( res ){
      var obj = res.toObject();
      uploaded = obj.uploaded;
    }
    done(err, uploaded);
  });
}

module.exports = s3;