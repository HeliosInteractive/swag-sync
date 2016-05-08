# swag-sync
Unidirectional S3 file sync utility (local to S3) with fault tolerance and caching features.

## Features
- Fault tolerance and caching failed uploads to be synchronized at a later time
- Concurrent uploads with an option to limit the number of concurrent tasks
- Recursive file-system watching mechanism to sync files in real time
- Configurable via command line options
- Cross-platform (Windows / Linux)

## Building
Open up Visual Studio (2015+) and do a *Rebuild All*. You can run the same binaries produced, under Linux with [`mono`](http://www.mono-project.com/).

## Running
*swag-sync* can be run in two modes: *standalone* or *sweep-once*. In both cases you need to have two environment variables defined: `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY`. **You also need SQLite3's binaries, which do not come with this utility and need to be [downloaded](https://www.sqlite.org/download.html) separately.**

### Standalone mode
In this mode, *swag-sync* can be thought as a "daemon" that watches your local directories and synchronize them with S3 buckets. Synchronization happens in two ways:

 1. In response to file-system watcher events (file creation / deletion / etc.)
 2. In response to scanning directories every once in a while

### Sweep-once mode
In this mode, application runs once, synchronizes all local files with S3 buckets and exits.

## Command line options
```TXT
-i, --interval=VALUE       The number of seconds for upload interval. This
                            must be an unsigned integer (number). Specify
                            zero to disable. DEFAULT: 10

-a, --aws_check_timeout=VALUE
                           Time in milliseconds to double check with S3 if
                            upload succeeds. This must be an unsigned integer (
                            number). Specify zero to disable. DEFAULT: 0

-p, --ping_interval=VALUE  Time in seconds to check for Internet connectivity.
                            This must be an unsigned integer (number).
                            Specify zero to disable. DEFAULT: 10

-d, --database_cleanup_interval=VALUE
                           Time in seconds to service the integrity of the
                            entire database. This must be an unsigned integer
                            (number). Specify zero to disable. DEFAULT: 10

-c, --count=VALUE          The number files to pop in every sweep PER BUCKET.
                            this must be an unsigned integer (number).
                            specify zero to disable. DEFAULT: 10

-b, --bucket_max=VALUE     Max number of parallel uploads PER BUCKET. This
                            must be an unsigned integer (number). DEFAULT: 10

-f, --fail_limit=VALUE     Number of attempts before giving up on a failed
                            upload. This must be an unsigned integer (number).
                            DEFAULT: 10

-r, --root=VALUE           The root directory to watch. Sub directories will
                            be used as bucket names.

-t, --timeout=VALUE        Timeout in seconds for upload operations. This must
                            be an unsigned integer (number). DEFAULT: 10

-v, --verbosity=VALUE      Log verbosity level Can be critical, info, warn,
                            or error. DEFAULT: critical

-s, --sweep                Sweep once and quit (Ignores database).
-h, --help                 show this message and exit
```

## Directory structure
You need to pass a root folder to *swag-sync*. All immediate sub-folders will be thought as buckets and everything inside them will be synced:
```TXT
+ root/
|
|- sub1/    <-- bucket name
|-- dir/
|---- file1 <-- will be synced
|---- file2 <-- will be synced
|-- file1   <-- will be synced
|-- file2   <-- will be synced
|
|- sub2/    <-- bucket name
|-- dir/    <-- empty, ignored until a file appears inside
|-- file1   <-- will be synced
|
|- file1    <-- will not be synced, no bucket
|- file2    <-- will not be synced, no bucket
```

## Sample bucket policy
```JSON
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["s3:listAllMyBuckets","s3:getBucketLocation"],
      "Resource": "arn:aws:s3::*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "s3:AbortMultipartUpload",
        "s3:CreateBucket",
        "s3:GetAccelerateConfiguration",
        "s3:GetBucketAcl",
        "s3:GetBucketCORS",
        "s3:GetBucketLocation",
        "s3:GetBucketLogging",
        "s3:GetBucketNotification",
        "s3:GetBucketPolicy",
        "s3:GetBucketRequestPayment",
        "s3:GetBucketTagging",
        "s3:GetBucketVersioning",
        "s3:GetBucketWebsite",
        "s3:GetLifecycleConfiguration",
        "s3:GetObject",
        "s3:GetObjectAcl",
        "s3:GetObjectTorrent",
        "s3:GetObjectVersion",
        "s3:GetObjectVersionAcl",
        "s3:GetObjectVersionTorrent",
        "s3:GetReplicationConfiguration",
        "s3:ListAllMyBuckets",
        "s3:ListBucket",
        "s3:ListBucketMultipartUploads",
        "s3:ListBucketVersions",
        "s3:ListMultipartUploadParts",
        "s3:PutAccelerateConfiguration",
        "s3:PutBucketAcl",
        "s3:PutBucketCORS",
        "s3:PutBucketLogging",
        "s3:PutBucketNotification",
        "s3:PutBucketPolicy",
        "s3:PutBucketRequestPayment",
        "s3:PutBucketTagging",
        "s3:PutBucketVersioning",
        "s3:PutBucketWebsite",
        "s3:PutLifecycleConfiguration",
        "s3:PutObject",
        "s3:PutObjectAcl",
        "s3:PutObjectVersionAcl",
        "s3:RestoreObject"
      ],
      "Resource": [
        "arn:aws:s3:::bucketname*",
      ]
    }
  ]
}
```
