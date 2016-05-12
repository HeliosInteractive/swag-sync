"use strict";

const fs = require('fs');
const path = require('path');
const EventEmitter = require('events');
const util = require('util');
const async = require('async');

/**
 * Watcher object
 *
 * Emits events
 *  - file.existing : a directory scan has found a file
 *  - file.new : a new file was found by the watcher
 * @param config
 * @constructor
 */
var Watcher = function(config){

  var self = this;
  var watching = new Map();
  var changed = new Map();

  const ignores = [
    /\.swp$/,
    /\.swx$/,
    /~$/,
  ];
  const iterateIgnores = ignores.length;

  /**
   *
   * @param filename
   * @returns {boolean}
   */
  function ingoreFile(filename){
    var i = 0;
    for(; i < iterateIgnores; i++)
      if( ignores[i].test(filename) )
        return true;

    return false;
  }

  /**
   * Watches a directory. Newly created directories also get watched
   * @param directory
   */
  function watch(directory){

    console.info(`watching ${directory}`);
    scanDir(directory, ( err, directories )=>{
      watchDirectories(directory, directories);
    });

    var toWatch = fs.watch(directory, {encoding: 'buffer'}, (event, filename) => {

      if( ingoreFile(filename) ) return;
      var file = path.join(directory, filename);
      fs.stat(file, (err, stats) => {

        if( err && err.code === 'ENOENT' ){
          var watcher = watching.get(file);
          if( watcher ){
            watcher.close();
            watching.delete(file);
            console.warn(`stopped watching ${file}`);
          }
          return;
        }
        if( stats.isDirectory() )
          return watch(file);

        if( !stats.isFile() )
          return;

        if( event === 'change'){
          var isChanging = changed.get(file);
          if( isChanging ) clearTimeout(isChanging);
          var changing = setTimeout(()=>{
            self.emit('file.new', file);
            changed.delete(file);
          }, 300);
          changed.set(file, changing);
        }
      });
    });

    watching.set(directory, toWatch);
  }

  /**
   * Looks in a directory to find any other directories
   * @param directory
   * @param done
   */
  function scanDir(directory, done){

    fs.readdir(directory, (err, files) => {

      if( !files || !files.length )
        return done(null, false);

      async.filter(files, function(filename, next){

        var file = path.join(directory, filename);
        fs.stat(file, (err, stats) => {
          if( stats.isFile() )
            self.emit('file.existing', file);
          next(err, stats.isDirectory());
        });
      }, done);
    });
  }

  /**
   * Given a base path and directory names it will watch those directories
   * @param base
   * @param directories
   */
  function watchDirectories(base, directories){

    directories && directories.forEach((folder) =>{
      watch(path.join(base, folder));
    });
  }

  /**
   * Initial scan for directories
   * @param directory
   */
  function init(directory){

    try{
      watch(directory);
    }catch(e){
      if( e.code === 'ENOENT'){
        console.error(`${directory} does not exist. waiting 60 seconds to retry`);
        setTimeout(()=>{
          init(directory);
        }, 60000);
      }
    }
  }
  init(config.watch);
};

util.inherits(Watcher, EventEmitter);

module.exports = function(config){
  return new Watcher(config);
};