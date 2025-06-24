ALTER TABLE `sa_servers` ADD COLUMN IF NOT EXISTS `rcon_password` varchar(128) NULL AFTER `hostname`;
