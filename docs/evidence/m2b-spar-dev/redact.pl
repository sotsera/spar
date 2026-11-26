#!/usr/bin/env perl
# Replaces the repository's path (in every form a Windows log holds it: E:/…, E:\…, JSON's E:\…, Git Bash's /e/…) with
# <repo>, and the user's profile folder with <USER>.
#   REPO=E:/path/to/repo perl redact.pl < raw > redacted
use strict;
use warnings;
my $forward = $ENV{REPO} or die "REPO is not set\n";
my $back = join(chr(92), split(m{/}, $forward, -1));
my $json = join(chr(92) x 2, split(m{/}, $forward, -1));
(my $posix = $forward) =~ s{^([A-Za-z]):}{"/" . lc($1)}e;
while (my $line = <STDIN>) {
    $line =~ s/\Q$json\E/<repo>/gi;
    $line =~ s/\Q$back\E/<repo>/gi;
    $line =~ s/\Q$forward\E/<repo>/gi;
    $line =~ s/\Q$posix\E/<repo>/gi;
    $line =~ s{C:\\Users\\[^\\"]+}{<USER>}gi;
    $line =~ s{C:[\/]Users[\/][^\/"\s]+}{<USER>}gi;
    print $line;
}
