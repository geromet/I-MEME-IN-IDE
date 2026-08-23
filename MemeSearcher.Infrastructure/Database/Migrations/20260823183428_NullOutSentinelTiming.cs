using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemeSearcher.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class NullOutSentinelTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert the old 0/0 sentinel to real nulls, but only for transcripts that genuinely
            // have no timeline. Scoped by transcript Source rather than by "both values are zero":
            // an SRT's first cue legitimately starts at 0.0, and blanket-nulling every zero would
            // destroy real timing to fix fake timing (#32).
            //
            // "text" is the FormatName of PlainTextTranscriptParser, the only parser that ever
            // emitted the sentinel.
            migrationBuilder.Sql("""
                UPDATE Segments SET StartSeconds = NULL, EndSeconds = NULL
                WHERE StartSeconds = 0 AND EndSeconds = 0
                  AND TranscriptId IN (SELECT Id FROM Transcripts WHERE Source = 'text');
                """);

            migrationBuilder.Sql("""
                UPDATE Words SET StartSeconds = NULL, EndSeconds = NULL
                WHERE StartSeconds = 0 AND EndSeconds = 0
                  AND SegmentId IN (
                      SELECT s.Id FROM Segments s
                      JOIN Transcripts t ON t.Id = s.TranscriptId
                      WHERE t.Source = 'text');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty: the sentinel carried no information, so there is nothing to
            // restore. Reversing this would mean inventing zeros again.
        }
    }
}
