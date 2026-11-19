# Redacts files in place for the evidence: the repository's path (as written, and as JSON escapes it) becomes <repo>,
# user profile paths become C:\Users\<user>, and short user names and SIDs are removed. REPO holds the repository's path.
use strict;
use warnings;

my $repo = $ENV{REPO};
(my $json = $repo) =~ s/\\/\\\\/g;
local $/;
for my $file (@ARGV) {
    open my $in, '<', $file or die "$file: $!";
    my $text = <$in>;
    close $in;
    $text =~ s/\Q$json\E/<repo>/gi;
    $text =~ s/\Q$repo\E/<repo>/gi;
    $text =~ s/C:(\\{1,2})Users\1[^\\" ]+/C:$1Users$1<user>/gi;
    $text =~ s/S-1-5-21-[0-9-]+/<sid>/g;
    $text =~ s/\b[A-Z]{6}~1\b/<user>/g;
    open my $out, '>', $file or die "$file: $!";
    print $out $text;
    close $out;
}
