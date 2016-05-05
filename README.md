# swag-sync
S3 file sync utility

## building
Open up Visual Studio and do a "Rebuild All". You can run the same binaries produced, under Linux with `mono`.

## sample bucket policy
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
