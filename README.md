# Reach Image Sync

Watches a directory and syncs the files with S3.

#### Features

 - First child nodes of the watched folder are considered buckets
 - Recursively watches the directory
 - All files put into the bucket directories are garaunteed to make it to S3 eventually
 - Failed files will be put in the back of the queue
 - Sweeps the watched folder on start to fill the queue
 - Files already uploaded (or failed) or marked in mongo db with status, path, and reason for failure (if any)

**Environment** settings

 - NODE_ENV : production or development _defaults_ `development`
 - PORT : runs a server on a port so you can verify running status _defaults_ `5000`
 - AWS_KEY : Access key for aws bucket(s)
 - AWS_SECRET : Secret key for aws bucket(s)
 - MONGO_URL : Location of mongo db to store state in _defaults_ `mongodb://127.0.0.1/imagesync`
 - CONCURRENCY : Max parallel uploads per bucket _defaults_ `5`
 - PATH : Absolute path on file system to watch
 - TIMEOUT : How long to wait (in milliseconds) for a file to upload before it's considered failed  _defaults_ `60000`

> `TIMEOUT` forcebly closes the request socket. Requests are retried infinitely. Set to -1 for no timeout.

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